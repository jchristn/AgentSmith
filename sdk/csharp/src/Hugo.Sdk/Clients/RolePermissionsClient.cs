namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;

    /// <summary>
    /// Role to permission mappings in the client's tenant (/v1.0/api/tenants/{tenantId}/role-permissions).
    /// </summary>
    public class RolePermissionsClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public RolePermissionsClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List mappings, optionally for one role.
        /// </summary>
        /// <param name="roleId">Role filter, or null for all.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EnumerationResult<RolePermissionMap>> ListAsync(string? roleId = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            Dictionary<string, string?> parameters = QueryBuilder.From(query);
            parameters["roleId"] = roleId;
            return GetAsync<EnumerationResult<RolePermissionMap>>(TenantPath("/role-permissions"), parameters, token);
        }

        /// <summary>
        /// Map a permission to a custom role.
        /// </summary>
        /// <param name="roleId">Role identifier.</param>
        /// <param name="permissionId">Permission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mapping.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<RolePermissionMap> CreateAsync(string roleId, string permissionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(roleId)) throw new ArgumentNullException(nameof(roleId));
            if (String.IsNullOrEmpty(permissionId)) throw new ArgumentNullException(nameof(permissionId));
            RolePermissionRequest request = new RolePermissionRequest();
            request.RoleId = roleId;
            request.PermissionId = permissionId;
            return PostAsync<RolePermissionMap>(TenantPath("/role-permissions"), request, token);
        }

        /// <summary>
        /// Remove a mapping.
        /// </summary>
        /// <param name="id">Mapping identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteAsync(TenantPath("/role-permissions/" + Segment(id)), null, token);
        }

        #endregion
    }
}
