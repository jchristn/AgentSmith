namespace Hugo.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// Role data access methods.
    /// </summary>
    public interface IRoleMethods
    {
        /// <summary>
        /// Create a Role, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<Role> CreateAsync(Role obj, CancellationToken token = default);

        /// <summary>
        /// Read a Role by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<Role?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a Role by identifier within a tenant. Built-in records (null tenant) are visible to every tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<Role?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a Role by name within a tenant. A tenant record wins over a built-in record with the same name.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null for built-in records only.</param>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<Role?> ReadByNameAsync(string? tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update a Role, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<Role> UpdateAsync(Role obj, CancellationToken token = default);

        /// <summary>
        /// Delete a Role and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a Role exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate Role records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<Role>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);
    }
}
