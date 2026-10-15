namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// AuthSession data access methods.
    /// </summary>
    public interface IAuthSessionMethods
    {
        /// <summary>
        /// Create a AuthSession, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<AuthSession> CreateAsync(AuthSession obj, CancellationToken token = default);

        /// <summary>
        /// Read a AuthSession by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<AuthSession?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a AuthSession by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<AuthSession?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a AuthSession, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<AuthSession> UpdateAsync(AuthSession obj, CancellationToken token = default);

        /// <summary>
        /// Delete a AuthSession and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a AuthSession exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate AuthSession records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<AuthSession>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Read a session by token identifier.
        /// </summary>
        /// <param name="tokenId">Token identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session or null.</returns>
        Task<AuthSession?> ReadByTokenIdAsync(string tokenId, CancellationToken token = default);

        /// <summary>
        /// Revoke a session.
        /// </summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a session was revoked.</returns>
        Task<bool> RevokeAsync(string id, string reason, CancellationToken token = default);



        /// <summary>
        /// Record a use of the session.
        /// </summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task TouchLastUsedAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete sessions that expired before a cutoff.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneExpiredAsync(DateTime olderThanUtc, CancellationToken token = default);

        /// <summary>
        /// Delete every session of a user.
        /// </summary>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Delete every session of a credential.
        /// </summary>
        /// <param name="credentialId">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteByCredentialAsync(string credentialId, CancellationToken token = default);
    }
}
