namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Secrets;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Builds the <see cref="ConnectorResourceContext"/> for a resource (P3.8): settings with schema defaults applied,
    /// secrets decrypted from the vault into a <see cref="SecretAccessor"/> that lives only for the call, a
    /// configuration hash (settings plus secret material) that keys pooled sessions, the server logger, the session
    /// pool, the knowledge store, and LearnSetting, which persists learned settings and audits them (HostKeyPinned for
    /// SSH host key fingerprints, D-028; SettingLearned otherwise). Thread-safe.
    /// </summary>
    public class ConnectorContextBuilder
    {
        #region Public-Members

        /// <summary>
        /// Raised with the tenant identifier after a learned setting or a knowledge write is persisted.
        /// </summary>
        public event EventHandler<string>? Changed;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly SecretService _Secrets;
        private readonly ConnectorRegistry _Registry;
        private readonly ConnectorSessionPool _Pool;
        private readonly AuditService _Audit;
        private readonly HugoLog _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="secrets">Secret service.</param>
        /// <param name="registry">Connector registry.</param>
        /// <param name="pool">Session pool.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ConnectorContextBuilder(DatabaseDriverBase database, SecretService secrets, ConnectorRegistry registry, ConnectorSessionPool pool, AuditService audit, HugoLog logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the context for a resource. Dispose the returned lease when the call ends.
        /// </summary>
        /// <param name="resource">Resource (saved, or unsaved for a settings test).</param>
        /// <param name="persisted">Whether the resource exists in the database (learned settings are persisted only then).</param>
        /// <param name="principal">Caller, for audit and knowledge attribution, or null.</param>
        /// <param name="writer">Name recorded on knowledge writes (for example "anderson@hugo.local via sql_update_context").</param>
        /// <param name="allowKnowledgeWrites">Whether knowledge writes are allowed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="includeSecrets">Whether to decrypt secrets. False builds a settings-only context (for classification,
        /// which runs before approval and must not touch secret material).</param>
        /// <returns>Lease.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ValidationException">Thrown when the connector type is not registered.</exception>
        public async Task<ConnectorContextLease> BuildAsync(Resource resource, bool persisted, RequestContext? principal, string writer, bool allowKnowledgeWrites, CancellationToken token, bool includeSecrets = true)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            IConnector connector = _Registry.Get(resource.ConnectorType) ?? throw new ValidationException("Connector type '" + resource.ConnectorType + "' is not registered.", new Dictionary<string, string> { { "connectorType", "Unknown connector type." } });
            Dictionary<string, string> settings = ConnectorSettingsValidator.ApplyDefaults(connector.Descriptor, resource.SettingValues());

            Dictionary<string, Dictionary<string, string>> slots = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            SecretScrubber scrubber = new SecretScrubber();
            StringBuilder hashInput = new StringBuilder();
            foreach (KeyValuePair<string, string> setting in settings.OrderBy(s => s.Key, StringComparer.Ordinal)) hashInput.Append(setting.Key).Append('=').Append(setting.Value).Append('\n');
            foreach (ResourceSecretBinding binding in resource.SecretBindings.Where(b => includeSecrets).OrderBy(b => b.SlotName, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                Dictionary<string, string> fields;
                try
                {
                    fields = await _Secrets.ResolveAsync(resource.TenantId, binding.SecretId, token).ConfigureAwait(false);
                }
                catch (NotFoundException)
                {
                    throw new ConnectorException(ConnectorErrorCodes.SecretMissing, "The secret bound to slot '" + binding.SlotName + "' no longer exists.");
                }
                Dictionary<string, string> slot = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> field in fields)
                {
                    if (!String.IsNullOrEmpty(binding.SecretFieldName) && !String.Equals(field.Key, binding.SecretFieldName, StringComparison.Ordinal)) continue;
                    slot[field.Key] = field.Value;
                    scrubber.AddKnownValue(field.Value);
                    hashInput.Append(binding.SlotName).Append('.').Append(field.Key).Append('=').Append(field.Value).Append('\n');
                }
                slots[binding.SlotName] = slot;
            }

            ConnectorResourceContext context = new ConnectorResourceContext();
            context.ResourceId = resource.Id;
            context.TenantId = resource.TenantId;
            context.Name = resource.Name;
            context.ConnectorType = resource.ConnectorType;
            context.Settings = new ResourceSettings(settings);
            context.Secrets = new SecretAccessor(slots);
            context.ConfigurationHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput.ToString()))).Substring(0, 16).ToLowerInvariant();
            context.Logger = new ServerConnectorLogger(_Logging, resource.ConnectorType, resource.Name, scrubber);
            context.Sessions = _Pool;
            if (persisted)
            {
                ServerResourceKnowledgeStore store = new ServerResourceKnowledgeStore(_Database, _Audit, resource.TenantId, resource.Id, principal, writer, allowKnowledgeWrites, scrubber);
                store.Changed += (sender, tenantId) => RaiseChanged(tenantId);
                context.Knowledge = store;
            }

            ConnectorContextLease lease = new ConnectorContextLease(context, scrubber);
            context.LearnSetting = (key, value) =>
            {
                if (String.IsNullOrEmpty(key)) return;
                lease.LearnedSettings[key] = value ?? String.Empty;
                if (persisted) lease.PendingWrites.Add(PersistLearnedAsync(connector, resource, principal, key, value ?? String.Empty));
            };
            return lease;
        }

        /// <summary>
        /// Audit a host key mismatch reported by a connector (HostKeyMismatch): the remote host presented a key other
        /// than the pinned one, which can mean a man-in-the-middle or a rebuilt host. Never throws.
        /// </summary>
        /// <param name="principal">Caller, or null.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier, or null.</param>
        /// <param name="resourceName">Resource name or alias for the description, or null.</param>
        /// <param name="message">The connector's (scrubbed) message.</param>
        /// <returns>Task.</returns>
        public async Task AuditHostKeyMismatchAsync(RequestContext? principal, string tenantId, string? resourceId, string? resourceName, string? message)
        {
            try
            {
                string description = "Host key mismatch on resource " + (resourceName ?? resourceId ?? "unknown") + ": " + (message ?? "the host presented a key other than the pinned one") + ".";
                await _Audit.WriteAsync(principal, AuditEventTypeEnum.HostKeyMismatch, "Resource", resourceId, description, tenantId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn("[Connectors] auditing a host key mismatch failed: " + SecretScrubber.Shared.Scrub(e.Message));
            }
        }

        #endregion

        #region Private-Methods

        private async Task PersistLearnedAsync(IConnector connector, Resource resource, RequestContext? principal, string key, string value)
        {
            try
            {
                SettingDescriptor? descriptor = connector.Descriptor.SettingsSchema.Find(key);
                SettingValueKindEnum kind = descriptor?.Kind ?? SettingValueKindEnum.String;
                await _Database.Resources.UpsertSettingAsync(resource.TenantId, resource.Id, key, kind, value, CancellationToken.None).ConfigureAwait(false);
                bool hostKey = key.IndexOf("hostkey", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("fingerprint", StringComparison.OrdinalIgnoreCase) >= 0;
                AuditEventTypeEnum eventType = hostKey ? AuditEventTypeEnum.HostKeyPinned : AuditEventTypeEnum.SettingLearned;
                string description = hostKey
                    ? "Pinned host key " + value + " for resource " + resource.Name + " on first use (setting " + key + ")."
                    : "Resource " + resource.Name + " learned setting " + key + ".";
                await _Audit.WriteAsync(principal, eventType, "Resource", resource.Id, description, resource.TenantId, CancellationToken.None).ConfigureAwait(false);
                RaiseChanged(resource.TenantId);
            }
            catch (Exception e)
            {
                _Logging.Warn("[Connectors] persisting learned setting " + key + " of resource " + resource.Id + " failed: " + SecretScrubber.Shared.Scrub(e.Message));
            }
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

        #endregion
    }
}
