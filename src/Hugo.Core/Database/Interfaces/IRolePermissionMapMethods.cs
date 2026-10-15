namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// RolePermissionMap data access methods.
    /// </summary>
    public interface IRolePermissionMapMethods
    {
        /// <summary>
        /// Create a RolePermissionMap, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<RolePermissionMap> CreateAsync(RolePermissionMap obj, CancellationToken token = default);

        /// <summary>
        /// Read a RolePermissionMap by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<RolePermissionMap?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a RolePermissionMap by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<RolePermissionMap?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a RolePermissionMap, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<RolePermissionMap> UpdateAsync(RolePermissionMap obj, CancellationToken token = default);

        /// <summary>
        /// Delete a RolePermissionMap and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a RolePermissionMap exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate RolePermissionMap records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<RolePermissionMap>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// List active permission mappings for a role.
        /// </summary>
        /// <param name="roleId">Role identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mappings.</returns>
        Task<List<RolePermissionMap>> ListByRoleAsync(string roleId, CancellationToken token = default);
    }
}
