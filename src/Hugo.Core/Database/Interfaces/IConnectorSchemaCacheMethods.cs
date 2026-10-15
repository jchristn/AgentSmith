namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Rebuildable connector cache entries (connector_schema_cache). Implementations must be thread-safe.
    /// </summary>
    public interface IConnectorSchemaCacheMethods
    {
        /// <summary>
        /// Read an unexpired entry.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="cacheKind">Cache kind.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry or null when absent or expired.</returns>
        Task<ConnectorSchemaCacheEntry?> ReadAsync(string tenantId, string resourceId, string cacheKind, CancellationToken token = default);

        /// <summary>
        /// Insert or replace an entry.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        Task PutAsync(ConnectorSchemaCacheEntry entry, CancellationToken token = default);

        /// <summary>
        /// Delete every entry of a resource.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteByResourceAsync(string tenantId, string resourceId, CancellationToken token = default);
    }
}
