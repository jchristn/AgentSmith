namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Secrets;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Resources (P3.6): create and update with settings schema validation, secret slot validation (slot declared, secret
    /// exists in the tenant, kind accepted), the connector's own ValidateAsync, visibility (Private resources are visible
    /// to their owner and administrators only), the access mode ceiling, duplicate, referrers, connection tests of saved
    /// and unsaved settings (cancelled after Connectors.TestTimeoutSeconds, D-036), and the cascading delete (plan
    /// section 7.7). Every change is audited (ResourceChanged), drops pooled sessions of the resource, and invalidates
    /// compiled instructions. Thread-safe.
    /// </summary>
    public class ResourceService
    {
        #region Public-Members

        /// <summary>
        /// Seconds after which a connection test is cancelled. Read on every test. Default 30.
        /// </summary>
        public Func<int> TestTimeoutSeconds
        {
            get
            {
                return _TestTimeoutSeconds;
            }
            set
            {
                _TestTimeoutSeconds = value ?? (() => 30);
            }
        }

        /// <summary>
        /// Seconds to wait for a resource's remote instructions (hugo_describe_resource on an external MCP server or a
        /// remote agent). Minimum 1, maximum 120, default 10. On timeout the description is returned without them.
        /// </summary>
        public int RemoteInstructionsTimeoutSeconds
        {
            get
            {
                return _RemoteInstructionsTimeoutSeconds;
            }
            set
            {
                _RemoteInstructionsTimeoutSeconds = Math.Clamp(value, 1, 120);
            }
        }

        /// <summary>
        /// Raised with the tenant identifier after any resource change.
        /// </summary>
        public event EventHandler<string>? Changed;

        #endregion

        #region Private-Members

        private static readonly Regex _NamePattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        private readonly ConnectorRegistry _Registry;
        private readonly SecretService _Secrets;
        private readonly ConnectorContextBuilder _Contexts;
        private readonly ConnectorSessionPool _Pool;
        private readonly AuditService _Audit;
        private readonly EventHub? _Events;
        private Func<int> _TestTimeoutSeconds = () => 30;
        private int _RemoteInstructionsTimeoutSeconds = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="registry">Connector registry.</param>
        /// <param name="secrets">Secret service.</param>
        /// <param name="contexts">Context builder.</param>
        /// <param name="pool">Session pool.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="events">Event hub, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ResourceService(DatabaseDriverBase database, ConnectorRegistry registry, SecretService secrets, ConnectorContextBuilder contexts, ConnectorSessionPool pool, AuditService audit, EventHub? events)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _Contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _Pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Events = events;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a string is a valid resource name.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidName(string? name)
        {
            return !String.IsNullOrEmpty(name) && _NamePattern.IsMatch(name);
        }

        /// <summary>
        /// Convert request setting values to strings (strings as is, numbers and booleans as JSON text, arrays as
        /// newline-separated lists; null removes the key).
        /// </summary>
        /// <param name="settings">Request settings.</param>
        /// <returns>String values.</returns>
        public static Dictionary<string, string> NormalizeSettings(Dictionary<string, JsonElement>? settings)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (settings == null) return values;
            foreach (KeyValuePair<string, JsonElement> setting in settings)
            {
                switch (setting.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        values[setting.Key] = setting.Value.GetString() ?? String.Empty;
                        break;
                    case JsonValueKind.Number:
                        values[setting.Key] = setting.Value.GetRawText();
                        break;
                    case JsonValueKind.True:
                        values[setting.Key] = "true";
                        break;
                    case JsonValueKind.False:
                        values[setting.Key] = "false";
                        break;
                    case JsonValueKind.Array:
                        List<string> items = new List<string>();
                        foreach (JsonElement item in setting.Value.EnumerateArray()) items.Add(item.ValueKind == JsonValueKind.String ? (item.GetString() ?? String.Empty) : item.GetRawText());
                        values[setting.Key] = String.Join("\n", items);
                        break;
                }
            }
            return values;
        }

        /// <summary>
        /// Create a resource.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resource.</returns>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="ConflictException">Thrown when the name is taken.</exception>
        public async Task<Resource> CreateAsync(RequestContext ctx, string tenantId, ResourceRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            if (!IsValidName(request.Name)) throw Invalid("name", "A name of 1 to 64 letters, digits, dots, hyphens, or underscores is required.");
            if (String.IsNullOrWhiteSpace(request.ConnectorType)) throw Invalid("connectorType", "A connector type is required.");
            if (await _Database.Resources.ReadByNameAsync(tenantId, request.Name!, token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A resource named '" + request.Name + "' already exists.");
            Resource resource = new Resource();
            resource.TenantId = tenantId;
            resource.OwnerUserId = ctx.UserId;
            resource.ConnectorType = request.ConnectorType!.Trim();
            Apply(resource, request, true);
            IConnector connector = Connector(resource.ConnectorType);
            resource.Settings = BuildSettings(connector, NormalizeSettings(request.Settings));
            resource.SecretBindings = request.SecretBindings == null ? new List<ResourceSecretBinding>() : CopySlots(request.SecretBindings);
            await ValidateAsync(ctx, connector, resource, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(resource.Endpoint)) resource.Endpoint = DeriveEndpoint(connector, resource);
            await _Database.Resources.CreateAsync(resource, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, resource, "resource.created", "Created " + resource.ConnectorType + " resource " + resource.Name + ".", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, resource.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a resource.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resource.</returns>
        /// <exception cref="NotFoundException">Thrown when the resource is not visible.</exception>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        public async Task<Resource> UpdateAsync(RequestContext ctx, string tenantId, string id, ResourceRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            Resource resource = await LoadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(request.ConnectorType) && !String.Equals(request.ConnectorType.Trim(), resource.ConnectorType, StringComparison.Ordinal)) throw Invalid("connectorType", "The connector type of a resource cannot change; create a new resource.");
            if (request.Name != null && request.Name != resource.Name)
            {
                if (!IsValidName(request.Name)) throw Invalid("name", "A name of 1 to 64 letters, digits, dots, hyphens, or underscores is required.");
                if (await _Database.Resources.ReadByNameAsync(tenantId, request.Name, token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A resource named '" + request.Name + "' already exists.");
            }
            IConnector connector = Connector(resource.ConnectorType);
            Apply(resource, request, false);
            if (request.Settings != null) resource.Settings = BuildSettings(connector, NormalizeSettings(request.Settings));
            if (request.SecretBindings != null) resource.SecretBindings = CopySlots(request.SecretBindings);
            await ValidateAsync(ctx, connector, resource, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(resource.Endpoint)) resource.Endpoint = DeriveEndpoint(connector, resource);
            await _Database.Resources.UpdateAsync(resource, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, resource, "resource.updated", "Updated resource " + resource.Name + ".", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, resource.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a resource visible to the caller, with secret names filled in.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resource.</returns>
        /// <exception cref="NotFoundException">Thrown when the resource does not exist or is not visible.</exception>
        public async Task<Resource> ReadAsync(RequestContext? ctx, string tenantId, string id, CancellationToken token = default)
        {
            Resource resource = await LoadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            foreach (ResourceSecretBinding slot in resource.SecretBindings)
            {
                Secret? secret = await _Database.Secrets.ReadAsync(tenantId, slot.SecretId, token).ConfigureAwait(false);
                slot.SecretName = secret?.Name;
            }
            return resource;
        }

        /// <summary>
        /// Enumerate resources visible to the caller.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="filter">Optional filters (connector type, environment, health), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page (private resources of others are removed from the page).</returns>
        public async Task<EnumerationResult<Resource>> EnumerateAsync(RequestContext ctx, string tenantId, EnumerationQuery query, ResourceFilter? filter, CancellationToken token = default)
        {
            EnumerationResult<Resource> page = await _Database.Resources.EnumerateAsync(tenantId, query, filter, token).ConfigureAwait(false);
            int before = page.Objects.Count;
            page.Objects = page.Objects.Where(r => ResourceVisibility.CanSee(r, ctx)).ToList();
            page.TotalRecords -= before - page.Objects.Count;
            return page;
        }

        /// <summary>
        /// Delete a resource with its settings, slots, bindings, schema cache, and knowledge.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted resource.</returns>
        /// <exception cref="NotFoundException">Thrown when the resource is not visible.</exception>
        public async Task<Resource> DeleteAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            Resource resource = await LoadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            await _Database.Resources.DeleteCascadeAsync(tenantId, id, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, resource, "resource.deleted", "Deleted resource " + resource.Name + " with its bindings, settings, secret slot bindings, schema cache, and knowledge.", token).ConfigureAwait(false);
            return resource;
        }

        /// <summary>
        /// Copy a resource under a new name (settings and secret slot bindings; not bindings or knowledge).
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Source resource identifier.</param>
        /// <param name="name">New name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The copy.</returns>
        public async Task<Resource> DuplicateAsync(RequestContext ctx, string tenantId, string id, string name, CancellationToken token = default)
        {
            Resource source = await LoadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            if (!IsValidName(name)) throw Invalid("name", "A name of 1 to 64 letters, digits, dots, hyphens, or underscores is required.");
            if (await _Database.Resources.ReadByNameAsync(tenantId, name, token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A resource named '" + name + "' already exists.");
            Resource copy = new Resource();
            copy.TenantId = tenantId;
            copy.OwnerUserId = ctx.UserId ?? source.OwnerUserId;
            copy.Visibility = source.Visibility;
            copy.Name = name;
            copy.ConnectorType = source.ConnectorType;
            copy.DisplayName = source.DisplayName;
            copy.Description = source.Description;
            copy.Endpoint = source.Endpoint;
            copy.Environment = source.Environment;
            copy.Notes = source.Notes;
            copy.DefaultAccessModeCeiling = source.DefaultAccessModeCeiling;
            copy.Active = source.Active;
            copy.Settings = source.Settings.Select(s => new ResourceSetting { Key = s.Key, ValueKind = s.ValueKind, Value = s.Value }).ToList();
            copy.SecretBindings = CopySlots(source.SecretBindings);
            await _Database.Resources.CreateAsync(copy, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, copy, "resource.created", "Duplicated resource " + source.Name + " as " + copy.Name + ".", token).ConfigureAwait(false);
            return await ReadAsync(ctx, tenantId, copy.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Bindings that use a resource.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Referrers.</returns>
        public async Task<List<ResourceReferrer>> ReferrersAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            Resource resource = await LoadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            List<Binding> bindings = await _Database.Bindings.ListByResourceAsync(tenantId, resource.Id, token).ConfigureAwait(false);
            List<ResourceReferrer> referrers = new List<ResourceReferrer>();
            foreach (Binding binding in bindings)
            {
                AgentProfile? profile = await _Database.AgentProfiles.ReadAsync(tenantId, binding.AgentProfileId, token).ConfigureAwait(false);
                ResourceReferrer referrer = new ResourceReferrer();
                referrer.AgentProfileId = binding.AgentProfileId;
                referrer.AgentProfileName = profile?.Name ?? binding.AgentProfileId;
                referrer.BindingId = binding.Id;
                referrer.Alias = binding.Alias;
                referrer.AccessMode = binding.AccessMode;
                referrer.Enabled = binding.Enabled;
                referrers.Add(referrer);
            }
            return referrers;
        }

        /// <summary>
        /// Test a saved resource and record the result.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public async Task<ResourceTestResponse> TestAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            Resource resource = await LoadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            ResourceTestResponse result = await ProbeAsync(ctx, resource, true, token).ConfigureAwait(false);
            await _Database.Resources.UpdateTestResultAsync(tenantId, id, result.Success, Truncate(result.Message, 2000), token).ConfigureAwait(false);
            _Events?.Publish("resource.tested", tenantId, new { id = resource.Id, name = resource.Name, success = result.Success, errorCode = result.ErrorCode });
            if (result.LearnedSettings.Count > 0) RaiseChanged(tenantId);
            return result;
        }

        /// <summary>
        /// Test unsaved settings. Nothing is stored, including settings the connector learns.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Resource as it would be created.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public async Task<ResourceTestResponse> TestUnsavedAsync(RequestContext ctx, string tenantId, ResourceRequest request, CancellationToken token = default)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.ConnectorType)) throw Invalid("connectorType", "A connector type is required.");
            IConnector connector = Connector(request.ConnectorType.Trim());
            Resource resource = new Resource();
            resource.TenantId = tenantId;
            resource.Name = IsValidName(request.Name) ? request.Name! : "unsaved";
            resource.ConnectorType = connector.Descriptor.Type;
            resource.Settings = BuildSettings(connector, NormalizeSettings(request.Settings));
            resource.SecretBindings = request.SecretBindings == null ? new List<ResourceSecretBinding>() : CopySlots(request.SecretBindings);
            ResourceTestResponse validation = await ValidationErrorsAsync(ctx, connector, resource, token).ConfigureAwait(false);
            if (validation.ValidationErrors.Count > 0) return validation;
            return await ProbeAsync(ctx, resource, false, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<Resource> LoadAsync(RequestContext? ctx, string tenantId, string id, CancellationToken token)
        {
            Resource? resource = await _Database.Resources.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (resource == null || !ResourceVisibility.CanSee(resource, ctx)) throw new NotFoundException("Resource not found.");
            return resource;
        }

        /// <summary>
        /// The third party's own guidance for a resource whose connector offers it (<see cref="IRemoteInstructionsProvider"/>),
        /// scrubbed and wrapped in an untrusted-content fence so a model treats it as data, never as instructions.
        /// </summary>
        /// <param name="ctx">Caller, or null.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="alias">Binding alias, used to label the fence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result; Text is null when the connector offers none, and Error is set when fetching failed.</returns>
        public async Task<RemoteInstructionsResult> RemoteInstructionsAsync(RequestContext? ctx, string tenantId, string resourceId, string alias, CancellationToken token = default)
        {
            RemoteInstructionsResult result = new RemoteInstructionsResult();
            Resource? resource = await _Database.Resources.ReadAsync(tenantId, resourceId, token).ConfigureAwait(false);
            if (resource == null) return result;
            if (_Registry.Get(resource.ConnectorType) is not IRemoteInstructionsProvider provider) return result;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(_RemoteInstructionsTimeoutSeconds));
                ConnectorContextLease? lease = null;
                try
                {
                    lease = await _Contexts.BuildAsync(resource, true, ctx, ctx?.PrincipalName ?? "describe", false, timeout.Token).ConfigureAwait(false);
                    string? text = await provider.GetRemoteInstructionsAsync(lease.Context, timeout.Token).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(text))
                    {
                        result.Text = UntrustedContent.Fence("instructions from " + resource.ConnectorType + " resource " + alias, lease.Scrubber.Scrub(text));
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    result.Error = ConnectorErrorCodes.Timeout;
                }
                catch (ConnectorException e)
                {
                    result.Error = e.Code;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    result.Error = ConnectorErrorCodes.ConnectionFailed;
                }
                finally
                {
                    lease?.Dispose();
                }
            }
            return result;
        }

        private IConnector Connector(string type)
        {
            IConnector? connector = _Registry.Get(type);
            if (connector == null) throw Invalid("connectorType", "Connector type '" + type + "' is not registered. GET /v1.0/api/connectors lists the available types.");
            return connector;
        }

        private static void Apply(Resource resource, ResourceRequest request, bool create)
        {
            if (request.Name != null) resource.Name = request.Name.Trim();
            if (request.DisplayName != null) resource.DisplayName = Empty(request.DisplayName);
            if (request.Description != null) resource.Description = Empty(request.Description);
            if (request.Endpoint != null) resource.Endpoint = Empty(request.Endpoint);
            if (request.Environment != null) resource.Environment = Empty(request.Environment);
            if (request.Notes != null) resource.Notes = Empty(request.Notes);
            if (request.Visibility.HasValue) resource.Visibility = request.Visibility.Value;
            if (request.DefaultAccessModeCeiling.HasValue) resource.DefaultAccessModeCeiling = request.DefaultAccessModeCeiling.Value;
            if (!create && request.ClearAccessModeCeiling) resource.DefaultAccessModeCeiling = null;
            if (request.Active.HasValue) resource.Active = request.Active.Value;
        }

        private static List<ResourceSetting> BuildSettings(IConnector connector, Dictionary<string, string> values)
        {
            List<ResourceSetting> settings = new List<ResourceSetting>();
            foreach (KeyValuePair<string, string> value in values.OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                if (String.IsNullOrWhiteSpace(value.Value)) continue;
                SettingDescriptor? descriptor = connector.Descriptor.SettingsSchema.Find(value.Key);
                ResourceSetting setting = new ResourceSetting();
                setting.Key = value.Key;
                setting.ValueKind = descriptor?.Kind ?? SettingValueKindEnum.String;
                setting.Value = value.Value.Trim();
                settings.Add(setting);
            }
            return settings;
        }

        private static List<ResourceSecretBinding> CopySlots(List<ResourceSecretBinding> slots)
        {
            List<ResourceSecretBinding> copy = new List<ResourceSecretBinding>();
            foreach (ResourceSecretBinding slot in slots.Where(s => s != null && !String.IsNullOrWhiteSpace(s.SlotName) && !String.IsNullOrWhiteSpace(s.SecretId)))
            {
                ResourceSecretBinding binding = new ResourceSecretBinding();
                binding.SlotName = slot.SlotName.Trim();
                binding.SecretId = slot.SecretId.Trim();
                binding.SecretFieldName = Empty(slot.SecretFieldName);
                copy.Add(binding);
            }
            return copy;
        }

        private async Task ValidateAsync(RequestContext ctx, IConnector connector, Resource resource, CancellationToken token)
        {
            ResourceTestResponse result = await ValidationErrorsAsync(ctx, connector, resource, token).ConfigureAwait(false);
            if (result.ValidationErrors.Count == 0) return;
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> codes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ConnectorValidationError error in result.ValidationErrors)
            {
                string field = String.IsNullOrEmpty(error.Field) ? "settings" : error.Field;
                if (!fields.ContainsKey(field))
                {
                    fields[field] = error.Message;
                    codes[field] = error.Code;
                }
            }
            ValidationException exception = new ValidationException("The resource settings are invalid: " + String.Join(" ", result.ValidationErrors.Select(e => e.Message)), fields, codes);
            exception.Code = "SettingsInvalid";
            throw exception;
        }

        private async Task<ResourceTestResponse> ValidationErrorsAsync(RequestContext ctx, IConnector connector, Resource resource, CancellationToken token)
        {
            ResourceTestResponse response = new ResourceTestResponse();
            if (resource.DefaultAccessModeCeiling.HasValue && !Enum.IsDefined(typeof(AccessModeEnum), resource.DefaultAccessModeCeiling.Value))
                response.ValidationErrors.Add(new ConnectorValidationError("defaultAccessModeCeiling", "invalid", "Unknown access mode."));
            Dictionary<string, SecretKindEnum> slotKinds = new Dictionary<string, SecretKindEnum>(StringComparer.Ordinal);
            foreach (ResourceSecretBinding slot in resource.SecretBindings)
            {
                if (slotKinds.ContainsKey(slot.SlotName))
                {
                    response.ValidationErrors.Add(new ConnectorValidationError(slot.SlotName, "duplicate_slot", "Secret slot '" + slot.SlotName + "' is bound twice."));
                    continue;
                }
                Secret? secret = await _Database.Secrets.ReadAsync(resource.TenantId, slot.SecretId, token).ConfigureAwait(false);
                if (secret == null)
                {
                    response.ValidationErrors.Add(new ConnectorValidationError(slot.SlotName, "secret_not_found", "Secret " + slot.SecretId + " bound to slot '" + slot.SlotName + "' does not exist in this tenant."));
                    continue;
                }
                if (!String.IsNullOrEmpty(slot.SecretFieldName) && !secret.Fields.Contains(slot.SecretFieldName, StringComparer.Ordinal))
                {
                    response.ValidationErrors.Add(new ConnectorValidationError(slot.SlotName, "field_not_found", "Secret " + secret.Name + " has no field '" + slot.SecretFieldName + "'."));
                    continue;
                }
                slotKinds[slot.SlotName] = secret.Kind;
            }
            ConnectorValidationResult schema = ConnectorSettingsValidator.Validate(connector.Descriptor, resource.SettingValues(), slotKinds);
            response.ValidationErrors.AddRange(schema.Errors);
            if (response.ValidationErrors.Count > 0)
            {
                response.Success = false;
                response.ErrorCode = ConnectorErrorCodes.InvalidSetting;
                response.Message = "The settings are invalid.";
                return response;
            }
            try
            {
                using (ConnectorContextLease lease = await _Contexts.BuildAsync(resource, false, ctx, ctx.PrincipalName ?? "operator", false, token).ConfigureAwait(false))
                {
                    ConnectorValidationResult own = await connector.ValidateAsync(lease.Context, token).ConfigureAwait(false);
                    foreach (ConnectorValidationError error in own.Errors)
                    {
                        response.ValidationErrors.Add(new ConnectorValidationError(error.Field, error.Code, lease.Scrubber.Scrub(error.Message) ?? String.Empty));
                    }
                }
            }
            catch (ConnectorException e)
            {
                response.ValidationErrors.Add(new ConnectorValidationError("secrets", e.Code, e.Message));
            }
            if (response.ValidationErrors.Count > 0)
            {
                response.Success = false;
                response.ErrorCode = ConnectorErrorCodes.InvalidSetting;
                response.Message = "The settings are invalid.";
            }
            return response;
        }

        private async Task<ResourceTestResponse> ProbeAsync(RequestContext ctx, Resource resource, bool persisted, CancellationToken token)
        {
            IConnector connector = Connector(resource.ConnectorType);
            ResourceTestResponse response = new ResourceTestResponse();
            long started = Stopwatch.GetTimestamp();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_TestTimeoutSeconds(), 1, 600)));
                ConnectorContextLease? lease = null;
                try
                {
                    lease = await _Contexts.BuildAsync(resource, persisted, ctx, ctx.PrincipalName ?? "operator", false, timeout.Token).ConfigureAwait(false);
                    ConnectorHealthResult health = await connector.TestConnectionAsync(lease.Context, timeout.Token).ConfigureAwait(false);
                    await lease.FlushAsync().ConfigureAwait(false);
                    response.Success = health.Success;
                    response.Message = lease.Scrubber.Scrub(health.Message) ?? String.Empty;
                    response.ErrorCode = health.ErrorCode;
                    if (!health.Success && health.ErrorCode == ConnectorErrorCodes.HostKeyMismatch) await _Contexts.AuditHostKeyMismatchAsync(ctx, resource.TenantId, persisted ? resource.Id : null, resource.Name, response.Message).ConfigureAwait(false);
                    response.LatencyMs = health.LatencyMs > 0 ? health.LatencyMs : Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    foreach (KeyValuePair<string, string> detail in health.Details ?? new Dictionary<string, string>()) response.Details[detail.Key] = lease.Scrubber.Scrub(detail.Value) ?? String.Empty;
                    foreach (KeyValuePair<string, string> learned in lease.LearnedSettings) response.LearnedSettings[learned.Key] = learned.Value;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    response.Success = false;
                    response.ErrorCode = ConnectorErrorCodes.Timeout;
                    response.Message = "The connection test did not finish within " + _TestTimeoutSeconds() + " seconds.";
                    response.LatencyMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                }
                catch (ConnectorException e)
                {
                    response.Success = false;
                    response.ErrorCode = e.Code;
                    response.Message = (lease?.Scrubber ?? SecretScrubber.Shared).Scrub(e.Message) ?? String.Empty;
                    response.LatencyMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (e.Code == ConnectorErrorCodes.HostKeyMismatch) await _Contexts.AuditHostKeyMismatchAsync(ctx, resource.TenantId, persisted ? resource.Id : null, resource.Name, response.Message).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    response.Success = false;
                    response.ErrorCode = ConnectorErrorCodes.ConnectionFailed;
                    response.Message = (lease?.Scrubber ?? SecretScrubber.Shared).Scrub(e.Message) ?? String.Empty;
                    response.LatencyMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                }
                finally
                {
                    lease?.Dispose();
                }
            }
            return response;
        }

        private static string DeriveEndpoint(IConnector connector, Resource resource)
        {
            Dictionary<string, string> values = ConnectorSettingsValidator.ApplyDefaults(connector.Descriptor, resource.SettingValues());
            if (values.TryGetValue("host", out string? host) && !String.IsNullOrEmpty(host))
            {
                return values.TryGetValue("port", out string? port) && !String.IsNullOrEmpty(port) ? host + ":" + port : host;
            }
            SettingDescriptor? summary = connector.Descriptor.SettingsSchema.Settings.FirstOrDefault(s => s.ShowInSummary && values.ContainsKey(s.Key));
            return summary == null ? String.Empty : Truncate(values[summary.Key], 500);
        }

        private async Task AfterChangeAsync(RequestContext ctx, Resource resource, string eventType, string description, CancellationToken token)
        {
            _Pool.InvalidateResource(resource.Id);
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ResourceChanged, "Resource", resource.Id, description, resource.TenantId, token).ConfigureAwait(false);
            _Events?.Publish(eventType, resource.TenantId, new { id = resource.Id, name = resource.Name, connectorType = resource.ConnectorType });
            RaiseChanged(resource.TenantId);
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

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }

        #endregion
    }
}
