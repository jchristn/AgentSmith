namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// CredentialScopeAssignment data access methods.
    /// </summary>
    public interface ICredentialScopeAssignmentMethods
    {
        /// <summary>
        /// Create a CredentialScopeAssignment, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<CredentialScopeAssignment> CreateAsync(CredentialScopeAssignment obj, CancellationToken token = default);

        /// <summary>
        /// Read a CredentialScopeAssignment by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<CredentialScopeAssignment?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a CredentialScopeAssignment by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<CredentialScopeAssignment?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a CredentialScopeAssignment, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<CredentialScopeAssignment> UpdateAsync(CredentialScopeAssignment obj, CancellationToken token = default);

        /// <summary>
        /// Delete a CredentialScopeAssignment and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a CredentialScopeAssignment exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate CredentialScopeAssignment records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<CredentialScopeAssignment>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// List active role assignments for a credential.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="credentialId">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        Task<List<CredentialScopeAssignment>> ListByCredentialAsync(string tenantId, string credentialId, CancellationToken token = default);
    }
}
