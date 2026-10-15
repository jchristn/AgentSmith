namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral connector cache data access. Thread-safe.
    /// </summary>
    public class AdoConnectorSchemaCacheMethods : IConnectorSchemaCacheMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoConnectorSchemaCacheMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("connector_schema_cache");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ConnectorSchemaCacheEntry?> ReadAsync(string tenantId, string resourceId, string cacheKind, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            if (String.IsNullOrEmpty(cacheKind)) throw new ArgumentNullException(nameof(cacheKind));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND cache_kind = @kind AND expires_utc > @now;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "resource_id", resourceId);
                AdoDatabaseDriver.Add(command, "kind", cacheKind);
                AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
            }, reader =>
            {
                ConnectorSchemaCacheEntry entry = new ConnectorSchemaCacheEntry();
                entry.Id = AdoConverters.Str(reader, "id");
                entry.TenantId = AdoConverters.Str(reader, "tenant_id");
                entry.ResourceId = AdoConverters.Str(reader, "resource_id");
                entry.CacheKind = AdoConverters.Str(reader, "cache_kind");
                entry.Content = AdoConverters.Str(reader, "content");
                entry.ExpiresUtc = AdoConverters.Date(reader, "expires_utc");
                entry.CreatedUtc = AdoConverters.Date(reader, "created_utc");
                entry.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
                return entry;
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutAsync(ConnectorSchemaCacheEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND cache_kind = @kind;", command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", entry.TenantId);
                    AdoDatabaseDriver.Add(command, "resource_id", entry.ResourceId);
                    AdoDatabaseDriver.Add(command, "kind", entry.CacheKind);
                }, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Table + " (id, tenant_id, resource_id, cache_kind, content, expires_utc, created_utc, last_update_utc) VALUES (@id, @tenant_id, @resource_id, @kind, @content, @expires_utc, @now, @now);", command =>
                {
                    AdoDatabaseDriver.Add(command, "id", entry.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", entry.TenantId);
                    AdoDatabaseDriver.Add(command, "resource_id", entry.ResourceId);
                    AdoDatabaseDriver.Add(command, "kind", entry.CacheKind);
                    AdoDatabaseDriver.Add(command, "content", entry.Content);
                    AdoDatabaseDriver.Add(command, "expires_utc", entry.ExpiresUtc);
                    AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByResourceAsync(string tenantId, string resourceId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            return await _Driver.NonQueryAsync("DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "resource_id", resourceId);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
