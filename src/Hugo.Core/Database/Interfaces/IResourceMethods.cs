namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;

    /// <summary>
    /// Resource data access (resources, resource_settings, resource_secret_bindings). Every read and write is tenant
    /// scoped. Implementations must be thread-safe.
    /// </summary>
    public interface IResourceMethods
    {
        /// <summary>
        /// Create a resource with its settings and secret slot bindings in one transaction.
        /// </summary>
        /// <param name="obj">Resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created resource.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<Resource> CreateAsync(Resource obj, CancellationToken token = default);

        /// <summary>
        /// Read a resource with its children.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resource or null when it does not exist in the tenant.</returns>
        Task<Resource?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a resource by name with its children.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resource or null.</returns>
        Task<Resource?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update a resource and replace its settings and secret slot bindings in one transaction.
        /// </summary>
        /// <param name="obj">Resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated resource.</returns>
        Task<Resource> UpdateAsync(Resource obj, CancellationToken token = default);

        /// <summary>
        /// Delete a resource with its settings, secret slot bindings, bindings (with their tool rules and deny patterns),
        /// schema cache, and knowledge, in one transaction (plan section 7.7).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the resource existed.</returns>
        Task<bool> DeleteCascadeAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate resources in a tenant, with their children.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Paging, search (name), and ordering.</param>
        /// <param name="filter">Optional filters (connector type, environment, health), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Resource>> EnumerateAsync(string tenantId, EnumerationQuery query, ResourceFilter? filter = null, CancellationToken token = default);

        /// <summary>
        /// Every resource in a tenant, with children.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resources ordered by name.</returns>
        Task<List<Resource>> ListAllAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Resources whose secret slots reference a secret.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="secretId">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resources (without children).</returns>
        Task<List<Resource>> ListBySecretAsync(string tenantId, string secretId, CancellationToken token = default);

        /// <summary>
        /// Record the outcome of a connection test.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Resource identifier.</param>
        /// <param name="success">Whether it succeeded.</param>
        /// <param name="message">Scrubbed message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateTestResultAsync(string tenantId, string id, bool success, string? message, CancellationToken token = default);

        /// <summary>
        /// Insert or replace one setting (used when a connector learns a setting, for example a pinned host key).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="key">Setting key.</param>
        /// <param name="kind">Value kind.</param>
        /// <param name="value">Value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertSettingAsync(string tenantId, string resourceId, string key, SettingValueKindEnum kind, string value, CancellationToken token = default);
    }
}
