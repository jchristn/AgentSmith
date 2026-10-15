namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;

    /// <summary>
    /// Tenant data access methods.
    /// </summary>
    public interface ITenantMethods
    {
        /// <summary>
        /// Create a Tenant, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<Tenant> CreateAsync(Tenant obj, CancellationToken token = default);

        /// <summary>
        /// Read a Tenant by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<Tenant?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a Tenant by name.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<Tenant?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Update a Tenant, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<Tenant> UpdateAsync(Tenant obj, CancellationToken token = default);

        /// <summary>
        /// Delete a Tenant and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a Tenant exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate Tenant records.
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<Tenant>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete a tenant and every tenant-owned row in every table that has a tenantid column, in one transaction.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Total rows deleted, including the tenant row.</returns>
        Task<int> DeleteCascadeAsync(string tenantId, CancellationToken token = default);
    }
}
