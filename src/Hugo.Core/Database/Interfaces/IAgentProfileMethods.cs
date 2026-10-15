namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// Agent profile data access. Every read and write is tenant scoped. Implementations must be thread-safe.
    /// </summary>
    public interface IAgentProfileMethods
    {
        /// <summary>
        /// Create a profile.
        /// </summary>
        /// <param name="obj">Profile.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<AgentProfile> CreateAsync(AgentProfile obj, CancellationToken token = default);

        /// <summary>
        /// Read a profile.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile or null.</returns>
        Task<AgentProfile?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a profile by name (case-insensitive).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile or null.</returns>
        Task<AgentProfile?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update a profile.
        /// </summary>
        /// <param name="obj">Profile.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated profile.</returns>
        Task<AgentProfile> UpdateAsync(AgentProfile obj, CancellationToken token = default);

        /// <summary>
        /// Delete a profile with its bindings (and their tool rules and deny patterns), and clear references to it from
        /// credentials, users, and the tenant, in one transaction. Resources are never deleted.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the profile existed.</returns>
        Task<bool> DeleteCascadeAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate profiles.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Paging, search (name), and ordering.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AgentProfile>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Every profile in a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profiles ordered by name.</returns>
        Task<List<AgentProfile>> ListAllAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Mark one profile as the tenant default: clears is_default on the others and sets the tenant's
        /// default_agent_profile_id, in one transaction.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetDefaultAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Profiles that reference a model endpoint (inference, fallback, or embedding).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="endpointId">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profiles.</returns>
        Task<List<AgentProfile>> ListByEndpointAsync(string tenantId, string endpointId, CancellationToken token = default);
    }
}
