namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral resource knowledge data access. Thread-safe.
    /// </summary>
    public class AdoResourceKnowledgeMethods : IResourceKnowledgeMethods
    {
        #region Public-Members

        /// <summary>Maximum optimistic retries for an unconditional operator write. Default 5, minimum 1, maximum 100.</summary>
        public int MaximumWriteAttempts
        {
            get { return _MaximumWriteAttempts; }
            set { _MaximumWriteAttempts = Math.Clamp(value, 1, 100); }
        }

        #endregion

        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _Versions;
        private int _MaximumWriteAttempts = 5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoResourceKnowledgeMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("resource_knowledge");
            _Versions = driver.Table("resource_knowledge_versions");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ResourceKnowledge?> ReadAsync(string tenantId, string resourceId, string scope, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND scope = @scope AND knowledge_key = @key;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "resource_id", resourceId);
                AdoDatabaseDriver.Add(command, "scope", scope ?? String.Empty);
                AdoDatabaseDriver.Add(command, "key", ResourceKnowledge.NormalizeKey(key));
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ResourceKnowledge>> ListAsync(string tenantId, string resourceId, string? scope, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id" + (scope == null ? String.Empty : " AND scope = @scope") + " ORDER BY scope ASC, knowledge_key ASC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "resource_id", resourceId);
                if (scope != null) AdoDatabaseDriver.Add(command, "scope", scope);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ResourceKnowledge> UpsertAsync(ResourceKnowledge entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            for (int attempt = 0; attempt < _MaximumWriteAttempts; attempt++)
            {
                ResourceKnowledge? stored = await WriteAsync(entry, null, token).ConfigureAwait(false);
                if (stored != null) return stored;
            }
            throw new InvalidOperationException("Resource knowledge changed repeatedly while saving; retry the operation.");
        }

        /// <inheritdoc />
        public Task<ResourceKnowledge?> TryUpsertAsync(ResourceKnowledge entry, int expectedVersion, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            return WriteAsync(entry, expectedVersion, token);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string resourceId, string scope, string key, string? deletedBy, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            for (int attempt = 0; attempt < _MaximumWriteAttempts; attempt++)
            {
                bool? deleted = await DeleteVersionAsync(tenantId, resourceId, scope, key, deletedBy, token).ConfigureAwait(false);
                if (deleted.HasValue) return deleted.Value;
            }
            throw new InvalidOperationException("Resource knowledge changed repeatedly while deleting; retry the operation.");
        }

        /// <inheritdoc />
        public async Task<List<ResourceKnowledgeVersion>> ListVersionsAsync(string tenantId, string resourceId, string scope, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Versions + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND scope = @scope AND knowledge_key = @key ORDER BY version DESC, created_utc DESC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "resource_id", resourceId);
                AdoDatabaseDriver.Add(command, "scope", scope ?? String.Empty);
                AdoDatabaseDriver.Add(command, "key", ResourceKnowledge.NormalizeKey(key));
            }, reader =>
            {
                ResourceKnowledgeVersion v = new ResourceKnowledgeVersion();
                v.Id = AdoConverters.Str(reader, "id");
                v.TenantId = AdoConverters.Str(reader, "tenant_id");
                v.KnowledgeId = AdoConverters.Str(reader, "knowledge_id");
                v.ResourceId = AdoConverters.Str(reader, "resource_id");
                v.Scope = AdoConverters.Str(reader, "scope");
                v.Key = AdoConverters.Str(reader, "knowledge_key");
                v.Version = AdoConverters.Int(reader, "version");
                v.Content = AdoConverters.Str(reader, "content");
                v.Source = AdoConverters.Enum(reader, "source", KnowledgeSourceEnum.Operator);
                v.UpdatedBy = AdoConverters.NullableStr(reader, "updated_by");
                v.Deleted = AdoConverters.Bool(reader, "deleted");
                v.CreatedUtc = AdoConverters.Date(reader, "created_utc");
                return v;
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to a knowledge entry.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Entry.</returns>
        public static ResourceKnowledge Map(DbDataReader reader)
        {
            ResourceKnowledge obj = new ResourceKnowledge();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.ResourceId = AdoConverters.Str(reader, "resource_id");
            obj.Scope = AdoConverters.Str(reader, "scope");
            obj.Key = AdoConverters.Str(reader, "knowledge_key");
            obj.Content = AdoConverters.Str(reader, "content");
            obj.Source = AdoConverters.Enum(reader, "source", KnowledgeSourceEnum.Operator);
            obj.UpdatedBy = AdoConverters.NullableStr(reader, "updated_by");
            obj.Version = AdoConverters.Int(reader, "version");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private async Task<bool?> DeleteVersionAsync(string tenantId, string resourceId, string scope, string key, string? deletedBy, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            bool? deleted = null;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                List<ResourceKnowledge> existing = await AdoDatabaseDriver.QueryAsync(connection, transaction, "SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND scope = @scope AND knowledge_key = @key;", command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "resource_id", resourceId);
                    AdoDatabaseDriver.Add(command, "scope", scope ?? String.Empty);
                    AdoDatabaseDriver.Add(command, "key", ResourceKnowledge.NormalizeKey(key));
                }, Map, ct).ConfigureAwait(false);
                if (existing.Count == 0) { deleted = false; return; }
                ResourceKnowledge current = existing[0];
                int affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE id = @id AND version = @version;", command =>
                {
                    AdoDatabaseDriver.Add(command, "id", current.Id);
                    AdoDatabaseDriver.Add(command, "version", current.Version);
                }, ct).ConfigureAwait(false);
                if (affected == 0) return;
                current.Version = current.Version + 1;
                current.UpdatedBy = deletedBy;
                await InsertVersionAsync(connection, transaction, current, true, ct).ConfigureAwait(false);
                deleted = true;
            }, token).ConfigureAwait(false);
            return deleted;
        }



        private async Task<ResourceKnowledge?> WriteAsync(ResourceKnowledge entry, int? expectedVersion, CancellationToken token)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            ResourceKnowledge? stored = null;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                List<ResourceKnowledge> existing = await AdoDatabaseDriver.QueryAsync(connection, transaction, "SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND scope = @scope AND knowledge_key = @key;", command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", entry.TenantId);
                    AdoDatabaseDriver.Add(command, "resource_id", entry.ResourceId);
                    AdoDatabaseDriver.Add(command, "scope", entry.Scope);
                    AdoDatabaseDriver.Add(command, "key", ResourceKnowledge.NormalizeKey(entry.Key));
                }, Map, ct).ConfigureAwait(false);
                if (expectedVersion.HasValue && (existing.Count == 0 ? 0 : existing[0].Version) != expectedVersion.Value) return;
                DateTime now = DateTime.UtcNow;
                if (existing.Count == 0)
                {
                    // A key that was deleted and written again continues its version history.
                    List<int> previous = await AdoDatabaseDriver.QueryAsync(connection, transaction, "SELECT MAX(version) AS v FROM " + _Versions + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND scope = @scope AND knowledge_key = @key;", command =>
                    {
                        AdoDatabaseDriver.Add(command, "tenant_id", entry.TenantId);
                        AdoDatabaseDriver.Add(command, "resource_id", entry.ResourceId);
                        AdoDatabaseDriver.Add(command, "scope", entry.Scope);
                        AdoDatabaseDriver.Add(command, "key", ResourceKnowledge.NormalizeKey(entry.Key));
                    }, reader => AdoConverters.Int(reader, "v"), ct).ConfigureAwait(false);
                    entry.Version = (previous.Count > 0 ? previous[0] : 0) + 1;
                    entry.CreatedUtc = now;
                    entry.LastUpdateUtc = now;
                    int inserted = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Table + " (id, tenant_id, resource_id, scope, knowledge_key, content, source, updated_by, version, created_utc, last_update_utc) VALUES (@id, @tenant_id, @resource_id, @scope, @key, @content, @source, @updated_by, @version, @created_utc, @last_update_utc) ON CONFLICT (resource_id, scope, knowledge_key) DO NOTHING;", command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", entry.Id);
                        AdoDatabaseDriver.Add(command, "tenant_id", entry.TenantId);
                        AdoDatabaseDriver.Add(command, "resource_id", entry.ResourceId);
                        AdoDatabaseDriver.Add(command, "scope", entry.Scope);
                        AdoDatabaseDriver.Add(command, "key", ResourceKnowledge.NormalizeKey(entry.Key));
                        AdoDatabaseDriver.Add(command, "content", entry.Content);
                        AdoDatabaseDriver.Add(command, "source", entry.Source.ToString());
                        AdoDatabaseDriver.Add(command, "updated_by", entry.UpdatedBy);
                        AdoDatabaseDriver.Add(command, "version", entry.Version);
                        AdoDatabaseDriver.Add(command, "created_utc", entry.CreatedUtc);
                        AdoDatabaseDriver.Add(command, "last_update_utc", entry.LastUpdateUtc);
                    }, ct).ConfigureAwait(false);
                    if (inserted == 0) return;
                }
                else
                {
                    ResourceKnowledge current = existing[0];
                    entry.Id = current.Id;
                    entry.CreatedUtc = current.CreatedUtc;
                    entry.Version = current.Version + 1;
                    entry.LastUpdateUtc = now;
                    int updated = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Table + " SET content = @content, source = @source, updated_by = @updated_by, version = @version, last_update_utc = @last_update_utc WHERE id = @id AND version = @expected_version;", command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", entry.Id);
                        AdoDatabaseDriver.Add(command, "expected_version", current.Version);
                        AdoDatabaseDriver.Add(command, "content", entry.Content);
                        AdoDatabaseDriver.Add(command, "source", entry.Source.ToString());
                        AdoDatabaseDriver.Add(command, "updated_by", entry.UpdatedBy);
                        AdoDatabaseDriver.Add(command, "version", entry.Version);
                        AdoDatabaseDriver.Add(command, "last_update_utc", entry.LastUpdateUtc);
                    }, ct).ConfigureAwait(false);
                    if (updated == 0) return;
                }
                await InsertVersionAsync(connection, transaction, entry, false, ct).ConfigureAwait(false);
                stored = entry;
            }, token).ConfigureAwait(false);
            return stored;
        }



        private async Task InsertVersionAsync(DbConnection connection, DbTransaction transaction, ResourceKnowledge entry, bool deleted, CancellationToken token)
        {
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Versions + " (id, tenant_id, knowledge_id, resource_id, scope, knowledge_key, version, content, source, updated_by, deleted, created_utc) VALUES (@id, @tenant_id, @knowledge_id, @resource_id, @scope, @key, @version, @content, @source, @updated_by, @deleted, @created_utc);", command =>
            {
                AdoDatabaseDriver.Add(command, "id", Hugo.Core.Helpers.IdGenerator.NewResourceKnowledgeVersionId());
                AdoDatabaseDriver.Add(command, "tenant_id", entry.TenantId);
                AdoDatabaseDriver.Add(command, "knowledge_id", entry.Id);
                AdoDatabaseDriver.Add(command, "resource_id", entry.ResourceId);
                AdoDatabaseDriver.Add(command, "scope", entry.Scope);
                AdoDatabaseDriver.Add(command, "key", ResourceKnowledge.NormalizeKey(entry.Key));
                AdoDatabaseDriver.Add(command, "version", entry.Version);
                AdoDatabaseDriver.Add(command, "content", deleted ? String.Empty : entry.Content);
                AdoDatabaseDriver.Add(command, "source", entry.Source.ToString());
                AdoDatabaseDriver.Add(command, "updated_by", entry.UpdatedBy);
                AdoDatabaseDriver.Add(command, "deleted", deleted);
                AdoDatabaseDriver.Add(command, "created_utc", DateTime.UtcNow);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
