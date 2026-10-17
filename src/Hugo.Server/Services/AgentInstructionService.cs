namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Knowledge;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Secrets;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Reads and atomically edits the current profile's persistent InstructionsAppend field, shared with the dashboard.
    /// The runtime must supply its current profile identifier rather than accepting a model-selected profile. Thread-safe.
    /// </summary>
    public class AgentInstructionService
    {
        private readonly DatabaseDriverBase _Database;
        private readonly AuthorizationService _Authorization;
        private readonly AuditService _Audit;
        private readonly InstructionCompiler _Compiler;
        private readonly EventHub? _Events;
        private readonly SecretService? _Secrets;

        /// <summary>
        /// Instantiate the instruction service.
        /// </summary>
        /// <param name="database">Persistence.</param>
        /// <param name="authorization">Authorization and bypass auditing.</param>
        /// <param name="audit">Change audit.</param>
        /// <param name="compiler">Compiled instruction cache.</param>
        /// <param name="events">Optional live event hub.</param>
        /// <param name="secrets">Optional vault resolver for complete bound-resource and model-credential scrubbing.</param>
        /// <exception cref="ArgumentNullException">Thrown for a missing required dependency.</exception>
        public AgentInstructionService(DatabaseDriverBase database, AuthorizationService authorization, AuditService audit, InstructionCompiler compiler, EventHub? events = null, SecretService? secrets = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _Events = events;
            _Secrets = secrets;
        }

        /// <summary>
        /// Read the complete current-profile instructions and their opaque concurrency version.
        /// </summary>
        /// <param name="ctx">Authenticated caller.</param>
        /// <param name="tenantId">Current turn tenant.</param>
        /// <param name="profileId">Current turn profile identifier, supplied by the runtime.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Full scrubbed instructions and version.</returns>
        public async Task<AgentInstructionResponse> ReadAsync(RequestContext ctx, string tenantId, string profileId, CancellationToken token = default)
        {
            AgentProfile profile = await AuthorizeAsync(ctx, tenantId, profileId, OperationTypeEnum.Read, token).ConfigureAwait(false);
            return Response(profile, await ScrubberAsync(ctx, profile, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Append or replace persistent instructions. Replacements require a read version; implicit appends retry at
        /// most three atomic comparisons. Changes never rewrite other profile fields or target resources.
        /// </summary>
        /// <param name="ctx">Authenticated caller.</param>
        /// <param name="tenantId">Current turn tenant.</param>
        /// <param name="profileId">Current turn profile identifier, supplied by the runtime.</param>
        /// <param name="request">Contribution, mode, and optional version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The persisted instructions and new version.</returns>
        public async Task<AgentInstructionResponse> UpdateAsync(RequestContext ctx, string tenantId, string profileId, AgentInstructionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            AgentProfile initial = await AuthorizeAsync(ctx, tenantId, profileId, OperationTypeEnum.Update, token).ConfigureAwait(false);
            SecretScrubber scopedScrubber = await ScrubberAsync(ctx, initial, token).ConfigureAwait(false);
            string mode = request.Mode ?? "append";
            string content = request.Content ?? String.Empty;
            if (mode != "append" && mode != "replace") throw Invalid("mode", "Use append or replace.");
            if (content.Length > 65536) throw Invalid("content", "Instructions may not exceed 65536 characters.");
            if (mode == "append" && String.IsNullOrWhiteSpace(content)) throw Invalid("content", "An appended instruction must not be empty.");
            if (mode == "replace" && String.IsNullOrWhiteSpace(request.ExpectedVersion)) throw Invalid("expectedVersion", "Read the current instructions and supply their version before replacing them.");
            if (content.Length > 0)
            {
                ResourceContextContentGuard guard = new ResourceContextContentGuard { MaxEntryChars = 65536 };
                string? refusal = guard.Check(content, null);
                if (refusal != null) throw Invalid("content", refusal);
                string scrubbed = scopedScrubber.Scrub(SecretScrubber.Shared.Scrub(content)) ?? String.Empty;
                if (!String.Equals(scrubbed, content, StringComparison.Ordinal)) throw Invalid("content", "Instructions appear to contain secret material. Remove credentials and sensitive values before saving.");
            }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                AgentProfile profile = await AuthorizeAsync(ctx, tenantId, profileId, OperationTypeEnum.Update, token).ConfigureAwait(false);
                Tenant tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) ?? throw new NotFoundException("Tenant not found.");
                if (tenant.Lockdown) throw new ForbiddenException("tenant_lockdown", "Persistent instruction updates are blocked while the tenant is in lockdown.");
                string previous = profile.InstructionsAppend ?? String.Empty;
                if (!String.IsNullOrEmpty(request.ExpectedVersion) && !String.Equals(request.ExpectedVersion, Version(previous), StringComparison.OrdinalIgnoreCase))
                    throw Conflict();
                string merged = mode == "append" && previous.Length > 0 ? previous + "\n\n" + content : content;
                if (merged.Length > 65536) throw Invalid("content", "The resulting persistent instructions exceed 65536 characters. Read and curate the existing instructions before retrying.");
                bool updated = await _Database.AgentProfiles.TryUpdateInstructionsAsync(tenantId, profileId, previous, merged, token).ConfigureAwait(false);
                if (!updated)
                {
                    if (mode == "append" && String.IsNullOrEmpty(request.ExpectedVersion)) continue;
                    throw Conflict();
                }
                _Compiler.Invalidate(tenantId);
                await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ProfileChanged, "AgentProfile", profileId, "Updated persistent agent instructions (mode " + mode + ", version " + Version(merged) + ").", tenantId, token).ConfigureAwait(false);
                _Events?.Publish("profile.updated", tenantId, new { id = profileId });
                profile.InstructionsAppend = merged;
                // Read only for the committed timestamp. Return the content/version of this write even if another writer
                // follows immediately; callers can detect that subsequent change with the returned version.
                AgentProfile? current = await _Database.AgentProfiles.ReadAsync(tenantId, profileId, token).ConfigureAwait(false);
                if (current != null) profile.LastUpdateUtc = current.LastUpdateUtc;
                return Response(profile, scopedScrubber);
            }
            throw Conflict();
        }

        private async Task<AgentProfile> AuthorizeAsync(RequestContext ctx, string tenantId, string profileId, OperationTypeEnum operation, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(profileId)) throw new ArgumentNullException(nameof(profileId));
            token.ThrowIfCancellationRequested();
            if (!ctx.IsAuthenticated) throw new ForbiddenException("permission_denied", "Authentication is required.");
            bool systemAdmin = ctx.IsAdmin || ctx.PrincipalType == PrincipalTypeEnum.Administrator;
            if (!systemAdmin && !String.Equals(ctx.TenantId, tenantId, StringComparison.Ordinal)) throw new NotFoundException("Agent profile not found.");
            AgentProfile? profile = await _Database.AgentProfiles.ReadAsync(tenantId, profileId, token).ConfigureAwait(false);
            if (profile == null || !profile.Active || !ResourceVisibility.CanSee(profile, ctx)) throw new NotFoundException("Agent profile not found.");
            if (ctx.Credential?.LockToProfile == true && !String.Equals(ctx.Credential.AgentProfileId, profileId, StringComparison.Ordinal))
                throw new ForbiddenException("permission_denied", "The credential is locked to another agent profile.");
            AuthorizationDecision access = await _Authorization.AuthorizeAsync(ctx, tenantId, ResourceTypeEnum.AgentProfile, operation, profileId, token).ConfigureAwait(false);
            if (!access.Permitted)
            {
                if (access.Result == AuthorizationResultEnum.NotFound) throw new NotFoundException("Agent profile not found.");
                throw new ForbiddenException("permission_denied", "This caller may not " + operation.ToString().ToLowerInvariant() + " this agent profile's persistent instructions.");
            }
            return profile;
        }

        private async Task<SecretScrubber> ScrubberAsync(RequestContext ctx, AgentProfile profile, CancellationToken token)
        {
            SecretScrubber scrubber = new SecretScrubber();
            if (_Secrets == null) return scrubber;
            HashSet<string> secretIds = new HashSet<string>(StringComparer.Ordinal);
            List<Binding> bindings = await _Database.Bindings.ListByProfileAsync(profile.TenantId, profile.Id, token).ConfigureAwait(false);
            foreach (Binding binding in bindings)
            {
                token.ThrowIfCancellationRequested();
                if (!binding.Enabled) continue;
                Resource? resource = await _Database.Resources.ReadAsync(profile.TenantId, binding.ResourceId, token).ConfigureAwait(false);
                if (resource == null || !resource.Active || !ResourceVisibility.CanSee(resource, ctx)) continue;
                foreach (ResourceSecretBinding slot in resource.SecretBindings) if (!String.IsNullOrEmpty(slot.SecretId)) secretIds.Add(slot.SecretId);
            }
            foreach (string? endpointId in new[] { profile.InferenceEndpointId, profile.FallbackInferenceEndpointId, profile.EmbeddingEndpointId })
            {
                if (String.IsNullOrEmpty(endpointId)) continue;
                ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(profile.TenantId, endpointId, token).ConfigureAwait(false);
                if (endpoint != null && !String.IsNullOrEmpty(endpoint.ApiKeySecretId)) secretIds.Add(endpoint.ApiKeySecretId);
            }
            foreach (string secretId in secretIds)
            {
                Dictionary<string, string> values = await _Secrets.ResolveAsync(profile.TenantId, secretId, token).ConfigureAwait(false);
                scrubber.AddKnownValues(values.Values);
            }
            return scrubber;
        }

        private static AgentInstructionResponse Response(AgentProfile profile, SecretScrubber scrubber)
        {
            string content = profile.InstructionsAppend ?? String.Empty;
            return new AgentInstructionResponse { ProfileId = profile.Id, Content = scrubber.Scrub(SecretScrubber.Shared.Scrub(content)) ?? String.Empty, Version = Version(content), LastUpdateUtc = profile.LastUpdateUtc };
        }

        private static string Version(string content)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        }

        private static ConflictException Conflict()
        {
            return new ConflictException("version_conflict", "Persistent instructions changed concurrently. Read them again before retrying.");
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException("Invalid persistent instructions: " + message, new Dictionary<string, string> { { field, message } });
        }
    }
}
