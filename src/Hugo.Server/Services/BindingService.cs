namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Requests;
    using Hugo.Core.Safety;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Bindings (P5.9): attach a resource to a profile under an alias with an access mode, approval policy, instruction
    /// override, output limits, tool rules, and command deny patterns. Aliases are unique per profile; the access mode
    /// may not exceed the resource's ceiling; tool rules must name tools of the resource's connector; deny patterns must
    /// be valid regular expressions. Changes are audited (BindingChanged). Thread-safe.
    /// </summary>
    public class BindingService
    {
        #region Public-Members

        /// <summary>
        /// Raised with the tenant identifier after any binding change.
        /// </summary>
        public event EventHandler<string>? Changed;

        #endregion

        #region Private-Members

        private static readonly Regex _AliasPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        private readonly ConnectorRegistry _Registry;
        private readonly AgentProfileService _Profiles;
        private readonly AuditService _Audit;
        private readonly EventHub? _Events;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="registry">Connector registry.</param>
        /// <param name="profiles">Profiles.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="events">Event hub, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public BindingService(DatabaseDriverBase database, ConnectorRegistry registry, AgentProfileService profiles, AuditService audit, EventHub? events)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Events = events;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Bindings of a profile.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bindings ordered by alias.</returns>
        public async Task<List<Binding>> ListAsync(RequestContext ctx, string tenantId, string profileId, CancellationToken token = default)
        {
            AgentProfile profile = await _Profiles.ReadAsync(ctx, tenantId, profileId, token).ConfigureAwait(false);
            return await _Database.Bindings.ListByProfileAsync(tenantId, profile.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one binding of a profile.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding.</returns>
        /// <exception cref="NotFoundException">Thrown when not found.</exception>
        public async Task<Binding> ReadAsync(RequestContext ctx, string tenantId, string profileId, string bindingId, CancellationToken token = default)
        {
            AgentProfile profile = await _Profiles.ReadAsync(ctx, tenantId, profileId, token).ConfigureAwait(false);
            Binding? binding = await _Database.Bindings.ReadAsync(tenantId, bindingId, token).ConfigureAwait(false);
            if (binding == null || binding.AgentProfileId != profile.Id) throw new NotFoundException("Binding not found.");
            return binding;
        }

        /// <summary>
        /// Bind a resource to a profile.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding.</returns>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="ConflictException">Thrown when the alias is taken in the profile.</exception>
        public async Task<Binding> CreateAsync(RequestContext ctx, string tenantId, string profileId, BindingRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            AgentProfile profile = await _Profiles.ReadAsync(ctx, tenantId, profileId, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(request.ResourceId)) throw Invalid("resourceId", "A resource is required.");
            Resource? resource = await _Database.Resources.ReadAsync(tenantId, request.ResourceId.Trim(), token).ConfigureAwait(false);
            if (resource == null || !ResourceVisibility.CanSee(resource, ctx)) throw Invalid("resourceId", "Resource " + request.ResourceId + " does not exist in this tenant.");
            Binding binding = new Binding();
            binding.TenantId = tenantId;
            binding.AgentProfileId = profile.Id;
            binding.ResourceId = resource.Id;
            binding.Alias = resource.Name;
            Apply(binding, request);
            await ValidateAsync(binding, resource, null, token).ConfigureAwait(false);
            await _Database.Bindings.CreateAsync(binding, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, binding, profile, "binding.created", "Bound resource " + resource.Name + " to profile " + profile.Name + " as " + binding.Alias + " (" + binding.AccessMode + ").", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, profile.Id, binding.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a binding.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding.</returns>
        public async Task<Binding> UpdateAsync(RequestContext ctx, string tenantId, string profileId, string bindingId, BindingRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            Binding binding = await ReadAsync(ctx, tenantId, profileId, bindingId, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(request.ResourceId) && !String.Equals(request.ResourceId.Trim(), binding.ResourceId, StringComparison.Ordinal)) throw Invalid("resourceId", "The resource of a binding cannot change; remove the binding and add a new one.");
            Resource resource = await _Database.Resources.ReadAsync(tenantId, binding.ResourceId, token).ConfigureAwait(false) ?? throw new NotFoundException("The bound resource no longer exists.");
            Apply(binding, request);
            await ValidateAsync(binding, resource, binding.Id, token).ConfigureAwait(false);
            await _Database.Bindings.UpdateAsync(binding, token).ConfigureAwait(false);
            AgentProfile profile = await _Profiles.ReadAsync(ctx, tenantId, profileId, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, binding, profile, "binding.updated", "Updated binding " + binding.Alias + " of profile " + profile.Name + " (" + binding.AccessMode + ", enabled " + binding.Enabled + ").", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, profileId, binding.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Replace a binding's tool rules.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="rules">Rules.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding.</returns>
        public async Task<Binding> SetToolRulesAsync(RequestContext ctx, string tenantId, string profileId, string bindingId, List<BindingToolRule> rules, CancellationToken token = default)
        {
            BindingRequest request = new BindingRequest();
            request.ToolRules = rules ?? new List<BindingToolRule>();
            return await UpdateAsync(ctx, tenantId, profileId, bindingId, request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a binding.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The removed binding.</returns>
        public async Task<Binding> DeleteAsync(RequestContext ctx, string tenantId, string profileId, string bindingId, CancellationToken token = default)
        {
            Binding binding = await ReadAsync(ctx, tenantId, profileId, bindingId, token).ConfigureAwait(false);
            AgentProfile profile = await _Profiles.ReadAsync(ctx, tenantId, profileId, token).ConfigureAwait(false);
            await _Database.Bindings.DeleteAsync(tenantId, bindingId, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, binding, profile, "binding.deleted", "Removed binding " + binding.Alias + " from profile " + profile.Name + ".", token).ConfigureAwait(false);
            return binding;
        }

        #endregion

        #region Private-Methods

        private static void Apply(Binding binding, BindingRequest request)
        {
            if (request.Alias != null) binding.Alias = request.Alias.Trim();
            if (request.AccessMode.HasValue) binding.AccessMode = request.AccessMode.Value;
            if (request.RequireApprovalForDestructive.HasValue) binding.RequireApprovalForDestructive = request.RequireApprovalForDestructive.Value;
            if (request.InstructionMode.HasValue) binding.InstructionMode = request.InstructionMode.Value;
            if (request.InstructionText != null) binding.InstructionText = String.IsNullOrWhiteSpace(request.InstructionText) ? null : request.InstructionText;
            if (request.MaxOutputBytes.HasValue) binding.MaxOutputBytes = request.MaxOutputBytes.Value <= 0 ? null : request.MaxOutputBytes.Value;
            if (request.MaxRows.HasValue) binding.MaxRows = request.MaxRows.Value <= 0 ? null : request.MaxRows.Value;
            if (request.Enabled.HasValue) binding.Enabled = request.Enabled.Value;
            if (request.AllowKnowledgeUpdates.HasValue) binding.AllowKnowledgeUpdates = request.AllowKnowledgeUpdates.Value;
            if (request.ToolRules != null) binding.ToolRules = request.ToolRules.Where(r => r != null && !String.IsNullOrWhiteSpace(r.ToolName)).Select(r => new BindingToolRule { ToolName = r.ToolName.Trim(), Rule = r.Rule }).ToList();
            if (request.DenyPatterns != null) binding.DenyPatterns = request.DenyPatterns.Where(p => p != null).ToList();
        }

        private async Task ValidateAsync(Binding binding, Resource resource, string? existingId, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!_AliasPattern.IsMatch(binding.Alias)) errors["alias"] = "The alias must be 1 to 64 letters, digits, dots, hyphens, or underscores.";
            List<Binding> siblings = await _Database.Bindings.ListByProfileAsync(binding.TenantId, binding.AgentProfileId, token).ConfigureAwait(false);
            if (siblings.Any(b => b.Id != existingId && String.Equals(b.Alias, binding.Alias, StringComparison.Ordinal)))
                throw new ConflictException("Duplicate", "The profile already has a resource bound as '" + binding.Alias + "'.");
            if (resource.DefaultAccessModeCeiling.HasValue && binding.AccessMode > resource.DefaultAccessModeCeiling.Value)
                errors["accessMode"] = "Resource " + resource.Name + " allows at most " + resource.DefaultAccessModeCeiling.Value + " access.";
            IConnector? connector = _Registry.Get(resource.ConnectorType);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BindingToolRule rule in binding.ToolRules)
            {
                if (!seen.Add(rule.ToolName)) errors["toolRules"] = "Tool " + rule.ToolName + " has more than one rule.";
                else if (connector != null && !connector.Tools.Any(t => String.Equals(t.Descriptor.Name, rule.ToolName, StringComparison.Ordinal))) errors["toolRules"] = "Connector " + resource.ConnectorType + " has no tool " + rule.ToolName + ".";
            }
            List<string> patternErrors = DenyPatternMatcher.Validate(binding.DenyPatterns);
            if (patternErrors.Count > 0) errors["denyPatterns"] = String.Join(" ", patternErrors);
            if (binding.InstructionText != null && binding.InstructionText.Length > 65536) errors["instructionText"] = "The instruction text is longer than 65536 characters.";
            if (errors.Count > 0) throw new ValidationException("The binding is invalid: " + String.Join(" ", errors.Values), errors);
        }

        private async Task AfterChangeAsync(RequestContext ctx, Binding binding, AgentProfile profile, string eventType, string description, CancellationToken token)
        {
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.BindingChanged, "Binding", binding.Id, description, binding.TenantId, token).ConfigureAwait(false);
            _Events?.Publish(eventType, binding.TenantId, new { id = binding.Id, agentProfileId = profile.Id, alias = binding.Alias, accessMode = binding.AccessMode.ToString() });
            EventHandler<string>? handler = Changed;
            if (handler == null) return;
            try
            {
                handler(this, binding.TenantId);
            }
            catch (Exception)
            {
            }
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }

        #endregion
    }
}
