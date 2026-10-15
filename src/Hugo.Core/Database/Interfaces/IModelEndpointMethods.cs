namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;

    /// <summary>
    /// Model endpoint data access. Implementations must be thread-safe.
    /// </summary>
    public interface IModelEndpointMethods
    {
        /// <summary>
        /// Create an endpoint.
        /// </summary>
        /// <param name="obj">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<ModelEndpoint> CreateAsync(ModelEndpoint obj, CancellationToken token = default);

        /// <summary>
        /// Read an endpoint.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint or null.</returns>
        Task<ModelEndpoint?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read an endpoint by name.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint or null.</returns>
        Task<ModelEndpoint?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update an endpoint.
        /// </summary>
        /// <param name="obj">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint.</returns>
        Task<ModelEndpoint> UpdateAsync(ModelEndpoint obj, CancellationToken token = default);

        /// <summary>
        /// Delete an endpoint and clear profile references to it, in one transaction.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it existed.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate endpoints.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Paging, search (name), and ordering.</param>
        /// <param name="filter">Optional filters (kind, provider, health), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<ModelEndpoint>> EnumerateAsync(string tenantId, EnumerationQuery query, ModelEndpointFilter? filter = null, CancellationToken token = default);

        /// <summary>
        /// Every active endpoint across tenants (for the health sweep).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints.</returns>
        Task<List<ModelEndpoint>> ListActiveAsync(CancellationToken token = default);

        /// <summary>
        /// Endpoints whose API key references a secret.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="secretId">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints.</returns>
        Task<List<ModelEndpoint>> ListBySecretAsync(string tenantId, string secretId, CancellationToken token = default);

        /// <summary>
        /// Record a health check.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="status">Status.</param>
        /// <param name="message">Scrubbed message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateHealthAsync(string tenantId, string id, ModelHealthStatusEnum status, string? message, CancellationToken token = default);
    }
}
