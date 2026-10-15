namespace Hugo.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// User data access methods.
    /// </summary>
    public interface IUserMethods
    {
        /// <summary>
        /// Create a User, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<User> CreateAsync(User obj, CancellationToken token = default);

        /// <summary>
        /// Read a User by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<User?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a User by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<User?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a User, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<User> UpdateAsync(User obj, CancellationToken token = default);

        /// <summary>
        /// Delete a User and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a User exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate User records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<User>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Read a user by email within a tenant (case-insensitive).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">Email.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User or null.</returns>
        Task<User?> ReadByEmailAsync(string tenantId, string email, CancellationToken token = default);

        /// <summary>
        /// Delete a user with the user's credentials, credential scope assignments, sessions, and role assignments, in
        /// one transaction (plan section 7.7).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteCascadeAsync(string tenantId, string userId, CancellationToken token = default);
    }
}
