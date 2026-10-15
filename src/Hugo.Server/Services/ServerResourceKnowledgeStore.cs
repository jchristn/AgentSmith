namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// The server's <see cref="IResourceKnowledgeStore"/> for one resource (P3.25, D-038): entries live in
    /// resource_knowledge with version history, content is scrubbed before it is stored (known secret values and secret
    /// patterns), every write is audited as KnowledgeChanged, and writes are refused when the binding does not allow
    /// knowledge updates. Thread-safe.
    /// </summary>
    public class ServerResourceKnowledgeStore : IResourceKnowledgeStore
    {
        #region Public-Members

        /// <summary>
        /// Maximum content length in characters. Default 262144.
        /// </summary>
        public int MaxContentLength { get; set; } = 262144;

        /// <summary>
        /// Raised after a write (put or delete) with the tenant identifier, so caches can be invalidated.
        /// </summary>
        public event EventHandler<string>? Changed;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuditService _Audit;
        private readonly string _TenantId;
        private readonly string _ResourceId;
        private readonly RequestContext? _Principal;
        private readonly string _Writer;
        private readonly bool _AllowWrites;
        private readonly SecretScrubber _Scrubber;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="principal">Caller, for audit, or null.</param>
        /// <param name="writer">Name recorded as updated_by (for example the principal and tool).</param>
        /// <param name="allowWrites">Whether writes are allowed (the binding's allow_knowledge_updates).</param>
        /// <param name="scrubber">Scrubber holding the invocation's secret values, or null for patterns only.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ServerResourceKnowledgeStore(DatabaseDriverBase database, AuditService audit, string tenantId, string resourceId, RequestContext? principal, string writer, bool allowWrites, SecretScrubber? scrubber)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            _TenantId = tenantId;
            _ResourceId = resourceId;
            _Principal = principal;
            _Writer = writer ?? String.Empty;
            _AllowWrites = allowWrites;
            _Scrubber = scrubber ?? SecretScrubber.Shared;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ResourceKnowledgeEntry?> GetAsync(string scope, string key, CancellationToken token = default)
        {
            ResourceKnowledge? stored = await _Database.ResourceKnowledge.ReadAsync(_TenantId, _ResourceId, scope ?? String.Empty, key ?? String.Empty, token).ConfigureAwait(false);
            return stored == null ? null : ToEntry(stored);
        }

        /// <inheritdoc />
        public async Task<List<ResourceKnowledgeEntry>> ListAsync(string? scope, CancellationToken token = default)
        {
            List<ResourceKnowledge> stored = await _Database.ResourceKnowledge.ListAsync(_TenantId, _ResourceId, scope, token).ConfigureAwait(false);
            return stored.Select(ToEntry).ToList();
        }

        /// <inheritdoc />
        public async Task<ResourceKnowledgeEntry> PutAsync(ResourceKnowledgeEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (!_AllowWrites) throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, "Knowledge updates are turned off for this resource on this profile (binding allowKnowledgeUpdates is false).");
            string content = _Scrubber.Scrub(entry.Content ?? String.Empty) ?? String.Empty;
            if (content.Length > MaxContentLength) throw new ConnectorException(ConnectorErrorCodes.TooLarge, "Knowledge content is longer than " + MaxContentLength + " characters.");
            ResourceKnowledge record = new ResourceKnowledge();
            record.TenantId = _TenantId;
            record.ResourceId = _ResourceId;
            record.Scope = entry.Scope ?? String.Empty;
            record.Key = entry.Key ?? String.Empty;
            record.Content = content;
            record.Source = entry.Source;
            record.UpdatedBy = String.IsNullOrEmpty(entry.UpdatedBy) ? _Writer : _Writer + " (" + entry.UpdatedBy + ")";
            ResourceKnowledge stored = await _Database.ResourceKnowledge.UpsertAsync(record, token).ConfigureAwait(false);
            await _Audit.WriteAsync(_Principal, AuditEventTypeEnum.KnowledgeChanged, "Resource", _ResourceId, "Knowledge " + Describe(record.Scope, record.Key) + " set to version " + stored.Version + " (" + stored.Source + ") by " + record.UpdatedBy + ".", _TenantId, token).ConfigureAwait(false);
            RaiseChanged();
            return ToEntry(stored);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string scope, string key, CancellationToken token = default)
        {
            if (!_AllowWrites) throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, "Knowledge updates are turned off for this resource on this profile (binding allowKnowledgeUpdates is false).");
            bool deleted = await _Database.ResourceKnowledge.DeleteAsync(_TenantId, _ResourceId, scope ?? String.Empty, key ?? String.Empty, _Writer, token).ConfigureAwait(false);
            if (deleted)
            {
                await _Audit.WriteAsync(_Principal, AuditEventTypeEnum.KnowledgeChanged, "Resource", _ResourceId, "Knowledge " + Describe(scope ?? String.Empty, key ?? String.Empty) + " deleted by " + _Writer + ".", _TenantId, token).ConfigureAwait(false);
                RaiseChanged();
            }
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static string Describe(string scope, string key)
        {
            return String.IsNullOrEmpty(key) ? scope + " (root)" : scope + "/" + key;
        }

        private static ResourceKnowledgeEntry ToEntry(ResourceKnowledge stored)
        {
            ResourceKnowledgeEntry entry = new ResourceKnowledgeEntry();
            entry.Scope = stored.Scope;
            entry.Key = stored.Key;
            entry.Content = stored.Content;
            entry.Source = stored.Source;
            entry.UpdatedBy = stored.UpdatedBy ?? String.Empty;
            entry.Version = stored.Version;
            entry.LastUpdateUtc = stored.LastUpdateUtc;
            return entry;
        }

        private void RaiseChanged()
        {
            EventHandler<string>? handler = Changed;
            if (handler == null) return;
            try
            {
                handler(this, _TenantId);
            }
            catch (Exception)
            {
                // Cache invalidation never breaks a write.
            }
        }

        #endregion
    }
}
