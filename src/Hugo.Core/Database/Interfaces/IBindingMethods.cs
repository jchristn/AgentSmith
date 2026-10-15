namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Binding data access (bindings, binding_tool_rules, binding_deny_patterns). Every read and write is tenant scoped.
    /// Implementations must be thread-safe.
    /// </summary>
    public interface IBindingMethods
    {
        /// <summary>
        /// Create a binding with its tool rules and deny patterns.
        /// </summary>
        /// <param name="obj">Binding.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created binding.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<Binding> CreateAsync(Binding obj, CancellationToken token = default);

        /// <summary>
        /// Read a binding with its children.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Binding identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding or null.</returns>
        Task<Binding?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a binding and replace its tool rules and deny patterns.
        /// </summary>
        /// <param name="obj">Binding.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated binding.</returns>
        Task<Binding> UpdateAsync(Binding obj, CancellationToken token = default);

        /// <summary>
        /// Delete a binding with its children.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Binding identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it existed.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Bindings of a profile with their children, ordered by alias.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bindings.</returns>
        Task<List<Binding>> ListByProfileAsync(string tenantId, string profileId, CancellationToken token = default);

        /// <summary>
        /// Bindings of a resource with their children.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bindings.</returns>
        Task<List<Binding>> ListByResourceAsync(string tenantId, string resourceId, CancellationToken token = default);

        /// <summary>
        /// Number of bindings per profile in a tenant. Profiles without bindings are absent from the result.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding count by profile identifier.</returns>
        Task<Dictionary<string, int>> CountByProfileAsync(string tenantId, CancellationToken token = default);
    }
}
