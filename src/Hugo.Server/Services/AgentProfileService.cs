namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Prompts;
    using Hugo.Core.Requests;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Agent profiles (P5.8, plan section 10.1): CRUD, duplicate (with bindings), visibility, the tenant default
    /// profile (every tenant gets a profile named Hugo at provisioning and at boot when it has none), and endpoint
    /// references. Deleting a profile removes its bindings and clears credential, user, and tenant references; resources
    /// are never deleted. Changes are audited (ProfileChanged). Thread-safe.
    /// </summary>
    public class AgentProfileService
    {
        #region Public-Members

        /// <summary>
        /// Name of the default profile created for every tenant. Default Hugo.
        /// </summary>
        public string DefaultProfileName { get; set; } = "Hugo";

        /// <summary>
        /// Raised with the tenant identifier after any profile change.
        /// </summary>
        public event EventHandler<string>? Changed;

        #endregion

        #region Private-Members

        private static readonly Regex _NamePattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        private readonly AuditService _Audit;
        private readonly EventHub? _Events;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="events">Event hub, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AgentProfileService(DatabaseDriverBase database, AuditService audit, EventHub? events)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Events = events;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a string is a valid profile name.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidName(string? name)
        {
            return !String.IsNullOrEmpty(name) && _NamePattern.IsMatch(name);
        }

        /// <summary>
        /// Make sure a tenant has a default profile: create Hugo (Agent Smith addressing Mr. Anderson) when the tenant
        /// has no profile, and point the tenant at a default when it has none.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The default profile.</returns>
        public async Task<AgentProfile> EnsureDefaultAsync(string tenantId, CancellationToken token = default)
        {
            Tenant tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) ?? throw new NotFoundException("Tenant not found.");
            List<AgentProfile> profiles = await _Database.AgentProfiles.ListAllAsync(tenantId, token).ConfigureAwait(false);
            AgentProfile? current = String.IsNullOrEmpty(tenant.DefaultAgentProfileId) ? null : profiles.FirstOrDefault(p => p.Id == tenant.DefaultAgentProfileId);
            if (current != null && current.IsDefault) return current;
            current = current ?? profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault(p => String.Equals(p.Name, DefaultProfileName, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                current = new AgentProfile();
                current.TenantId = tenantId;
                current.Name = DefaultProfileName;
                current.DisplayName = DefaultProfileName;
                current.Description = "Default operations agent. Attach resources to give Agent Smith something to work with.";
                current.IsDefault = true;
                await _Database.AgentProfiles.CreateAsync(current, token).ConfigureAwait(false);
                await _Audit.WriteAsync(null, AuditEventTypeEnum.ProfileChanged, "AgentProfile", current.Id, "Created the default profile " + current.Name + " for tenant " + tenant.Name + ".", tenantId, token).ConfigureAwait(false);
            }
            await _Database.AgentProfiles.SetDefaultAsync(tenantId, current.Id, token).ConfigureAwait(false);
            current.IsDefault = true;
            RaiseChanged(tenantId);
            return current;
        }

        /// <summary>
        /// Create a profile.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile.</returns>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="ConflictException">Thrown when the name is taken.</exception>
        public async Task<AgentProfile> CreateAsync(RequestContext ctx, string tenantId, AgentProfileRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            if (!IsValidName(request.Name)) throw Invalid("name", "A URL-safe name of 1 to 64 letters, digits, hyphens, or underscores is required.");
            if (await _Database.AgentProfiles.ReadByNameAsync(tenantId, request.Name!, token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A profile named '" + request.Name + "' already exists.");
            AgentProfile profile = new AgentProfile();
            profile.TenantId = tenantId;
            profile.OwnerUserId = ctx.UserId;
            await ApplyAsync(profile, request, token).ConfigureAwait(false);
            profile.IsDefault = false;
            await _Database.AgentProfiles.CreateAsync(profile, token).ConfigureAwait(false);
            if (request.IsDefault == true) await _Database.AgentProfiles.SetDefaultAsync(tenantId, profile.Id, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, profile, "profile.created", "Created agent profile " + profile.Name + ".", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, profile.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a profile.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Profile identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile.</returns>
        public async Task<AgentProfile> UpdateAsync(RequestContext ctx, string tenantId, string id, AgentProfileRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            AgentProfile profile = await ReadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            if (request.Name != null && !String.Equals(request.Name, profile.Name, StringComparison.Ordinal))
            {
                if (!IsValidName(request.Name)) throw Invalid("name", "A URL-safe name of 1 to 64 letters, digits, hyphens, or underscores is required.");
                AgentProfile? other = await _Database.AgentProfiles.ReadByNameAsync(tenantId, request.Name, token).ConfigureAwait(false);
                if (other != null && other.Id != profile.Id) throw new ConflictException("Duplicate", "A profile named '" + request.Name + "' already exists.");
            }
            if (request.Active == false && profile.IsDefault) throw new ConflictException("DefaultProfile", "The tenant's default profile cannot be deactivated; make another profile the default first.");
            await ApplyAsync(profile, request, token).ConfigureAwait(false);
            await _Database.AgentProfiles.UpdateAsync(profile, token).ConfigureAwait(false);
            if (request.IsDefault == true && !profile.IsDefault) await _Database.AgentProfiles.SetDefaultAsync(tenantId, profile.Id, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, profile, "profile.updated", "Updated agent profile " + profile.Name + ".", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, profile.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a profile visible to the caller.
        /// </summary>
        /// <param name="ctx">Caller, or null for an internal caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile.</returns>
        /// <exception cref="NotFoundException">Thrown when not found or not visible.</exception>
        public async Task<AgentProfile> ReadAsync(RequestContext? ctx, string tenantId, string id, CancellationToken token = default)
        {
            AgentProfile? profile = await _Database.AgentProfiles.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (profile == null || !ResourceVisibility.CanSee(profile, ctx)) throw new NotFoundException("Agent profile not found.");
            return profile;
        }

        /// <summary>
        /// Enumerate profiles visible to the caller.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<AgentProfile>> EnumerateAsync(RequestContext ctx, string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            EnumerationResult<AgentProfile> page = await _Database.AgentProfiles.EnumerateAsync(tenantId, query, token).ConfigureAwait(false);
            int before = page.Objects.Count;
            page.Objects = page.Objects.Where(p => ResourceVisibility.CanSee(p, ctx)).ToList();
            page.TotalRecords -= before - page.Objects.Count;
            return page;
        }

        /// <summary>
        /// Delete a profile with its bindings.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted profile.</returns>
        /// <exception cref="ConflictException">Thrown when the profile is the tenant default.</exception>
        public async Task<AgentProfile> DeleteAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            AgentProfile profile = await ReadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            if (profile.IsDefault) throw new ConflictException("DefaultProfile", "The tenant's default profile cannot be deleted; make another profile the default first.");
            await _Database.AgentProfiles.DeleteCascadeAsync(tenantId, id, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, profile, "profile.deleted", "Deleted agent profile " + profile.Name + " and its bindings.", token).ConfigureAwait(false);
            return profile;
        }

        /// <summary>
        /// Copy a profile with its bindings under a new name.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Source profile identifier.</param>
        /// <param name="name">New name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The copy.</returns>
        public async Task<AgentProfile> DuplicateAsync(RequestContext ctx, string tenantId, string id, string name, CancellationToken token = default)
        {
            AgentProfile source = await ReadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            if (!IsValidName(name)) throw Invalid("name", "A URL-safe name of 1 to 64 letters, digits, hyphens, or underscores is required.");
            if (await _Database.AgentProfiles.ReadByNameAsync(tenantId, name, token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A profile named '" + name + "' already exists.");
            AgentProfile copy = Serialization(source);
            copy.Id = String.Empty;
            copy.Name = name;
            copy.DisplayName = name;
            copy.IsDefault = false;
            copy.OwnerUserId = ctx.UserId ?? source.OwnerUserId;
            await _Database.AgentProfiles.CreateAsync(copy, token).ConfigureAwait(false);
            foreach (Binding binding in await _Database.Bindings.ListByProfileAsync(tenantId, source.Id, token).ConfigureAwait(false))
            {
                Binding clone = new Binding();
                clone.TenantId = tenantId;
                clone.AgentProfileId = copy.Id;
                clone.ResourceId = binding.ResourceId;
                clone.Alias = binding.Alias;
                clone.AccessMode = binding.AccessMode;
                clone.RequireApprovalForDestructive = binding.RequireApprovalForDestructive;
                clone.InstructionMode = binding.InstructionMode;
                clone.InstructionText = binding.InstructionText;
                clone.MaxOutputBytes = binding.MaxOutputBytes;
                clone.MaxRows = binding.MaxRows;
                clone.Enabled = binding.Enabled;
                clone.AllowKnowledgeUpdates = binding.AllowKnowledgeUpdates;
                clone.ToolRules = binding.ToolRules.Select(r => new BindingToolRule { ToolName = r.ToolName, Rule = r.Rule }).ToList();
                clone.DenyPatterns = new List<string>(binding.DenyPatterns);
                await _Database.Bindings.CreateAsync(clone, token).ConfigureAwait(false);
            }
            await AfterChangeAsync(ctx, copy, "profile.created", "Duplicated agent profile " + source.Name + " as " + copy.Name + ".", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, copy.Id, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task ApplyAsync(AgentProfile profile, AgentProfileRequest request, CancellationToken token)
        {
            if (request.Name != null) profile.Name = request.Name.Trim();
            if (request.DisplayName != null) profile.DisplayName = Empty(request.DisplayName);
            if (request.PersonaName != null) profile.PersonaName = String.IsNullOrWhiteSpace(request.PersonaName) ? "Agent Smith" : Limit(request.PersonaName.Trim(), 128, "personaName");
            if (request.UserAddress != null) profile.UserAddress = String.IsNullOrWhiteSpace(request.UserAddress) ? "Mr. Anderson" : Limit(request.UserAddress.Trim(), 128, "userAddress");
            if (request.Description != null) profile.Description = Empty(request.Description);
            if (request.PersonaTemplateKey != null)
            {
                string key = String.IsNullOrWhiteSpace(request.PersonaTemplateKey) ? BuiltInTemplateCatalog.DefaultPersonaKey : request.PersonaTemplateKey.Trim();
                if (!PromptTemplateService.IsValidKey(key)) throw Invalid("personaTemplateKey", "The persona template key must be a template key such as persona.agent-smith.");
                profile.PersonaTemplateKey = key;
            }
            if (request.InferenceEndpointId != null) profile.InferenceEndpointId = await EndpointAsync(profile.TenantId, request.InferenceEndpointId, ModelKindEnum.Inference, "inferenceEndpointId", token).ConfigureAwait(false);
            if (request.FallbackInferenceEndpointId != null) profile.FallbackInferenceEndpointId = await EndpointAsync(profile.TenantId, request.FallbackInferenceEndpointId, ModelKindEnum.Inference, "fallbackInferenceEndpointId", token).ConfigureAwait(false);
            if (request.EmbeddingEndpointId != null) profile.EmbeddingEndpointId = await EndpointAsync(profile.TenantId, request.EmbeddingEndpointId, ModelKindEnum.Embedding, "embeddingEndpointId", token).ConfigureAwait(false);
            if (request.MaxIterations.HasValue) profile.MaxIterations = request.MaxIterations.Value;
            if (request.MaxToolCallsPerTurn.HasValue) profile.MaxToolCallsPerTurn = request.MaxToolCallsPerTurn.Value;
            if (request.ToolTimeoutSeconds.HasValue) profile.ToolTimeoutSeconds = request.ToolTimeoutSeconds.Value;
            if (request.Temperature.HasValue) profile.Temperature = Math.Clamp(request.Temperature.Value, 0, 2);
            if (request.ExposeA2A.HasValue) profile.ExposeA2A = request.ExposeA2A.Value;
            if (request.ExposeMcp.HasValue) profile.ExposeMcp = request.ExposeMcp.Value;
            if (request.ExposeOpenAI.HasValue) profile.ExposeOpenAI = request.ExposeOpenAI.Value;
            if (request.ExposeManagementTools.HasValue) profile.ExposeManagementTools = request.ExposeManagementTools.Value;
            if (request.InstructionsAppend != null) profile.InstructionsAppend = String.IsNullOrWhiteSpace(request.InstructionsAppend) ? null : Limit(request.InstructionsAppend, 65536, "instructionsAppend");
            if (request.Visibility.HasValue) profile.Visibility = request.Visibility.Value;
            if (request.Active.HasValue) profile.Active = request.Active.Value;
        }

        private async Task<string?> EndpointAsync(string tenantId, string id, ModelKindEnum kind, string field, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(tenantId, id.Trim(), token).ConfigureAwait(false);
            if (endpoint == null) throw Invalid(field, "Model endpoint " + id + " does not exist in this tenant.");
            if (endpoint.Kind != kind) throw Invalid(field, "Model endpoint " + endpoint.Name + " is an " + endpoint.Kind + " endpoint, not " + kind + ".");
            return endpoint.Id;
        }

        private async Task AfterChangeAsync(RequestContext ctx, AgentProfile profile, string eventType, string description, CancellationToken token)
        {
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ProfileChanged, "AgentProfile", profile.Id, description, profile.TenantId, token).ConfigureAwait(false);
            _Events?.Publish(eventType, profile.TenantId, new { id = profile.Id, name = profile.Name });
            RaiseChanged(profile.TenantId);
        }

        private void RaiseChanged(string tenantId)
        {
            EventHandler<string>? handler = Changed;
            if (handler == null) return;
            try
            {
                handler(this, tenantId);
            }
            catch (Exception)
            {
            }
        }

        private static AgentProfile Serialization(AgentProfile source)
        {
            return Hugo.Core.Serialization.Serializer.Copy(source) ?? throw new InvalidOperationException("The profile could not be copied.");
        }

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Limit(string value, int max, string field)
        {
            if (value.Length > max) throw Invalid(field, field + " is longer than " + max + " characters.");
            return value;
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }

        #endregion
    }
}
