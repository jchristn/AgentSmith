namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// UserRoleAssignment data access methods.
    /// </summary>
    public interface IUserRoleAssignmentMethods
    {
        /// <summary>
        /// Create a UserRoleAssignment, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<UserRoleAssignment> CreateAsync(UserRoleAssignment obj, CancellationToken token = default);

        /// <summary>
        /// Read a UserRoleAssignment by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<UserRoleAssignment?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a UserRoleAssignment by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<UserRoleAssignment?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a UserRoleAssignment, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<UserRoleAssignment> UpdateAsync(UserRoleAssignment obj, CancellationToken token = default);

        /// <summary>
        /// Delete a UserRoleAssignment and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a UserRoleAssignment exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate UserRoleAssignment records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<UserRoleAssignment>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// List active role assignments for a user.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        Task<List<UserRoleAssignment>> ListByUserAsync(string tenantId, string userId, CancellationToken token = default);
    }
}
