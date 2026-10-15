namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;

    /// <summary>
    /// Approval data access. Decisions are atomic transitions out of Pending, so concurrent deciders, the expiry job,
    /// and lockdown cannot both win. Implementations must be thread-safe.
    /// </summary>
    public interface IApprovalMethods
    {
        /// <summary>
        /// Insert an approval.
        /// </summary>
        /// <param name="obj">Approval.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The approval.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<Approval> CreateAsync(Approval obj, CancellationToken token = default);

        /// <summary>
        /// Read an approval.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approval or null.</returns>
        Task<Approval?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Atomically move a Pending approval to a final status.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="status">New status (Approved, Rejected, Expired, or Cancelled).</param>
        /// <param name="decidedById">Who decided, or null.</param>
        /// <param name="decidedByName">Display name of who decided, or null.</param>
        /// <param name="note">Decision note, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this caller made the transition.</returns>
        Task<bool> TryDecideAsync(string tenantId, string id, ApprovalStatusEnum status, string? decidedById, string? decidedByName, string? note, CancellationToken token = default);

        /// <summary>
        /// Enumerate approvals, newest first by default.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Paging and ordering; search matches the summary.</param>
        /// <param name="filter">Filters (ApprovalStatus, AgentProfileId, ResourceId, PrincipalId).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Approval>> EnumerateAsync(string tenantId, EnumerationQuery query, ToolInvocationFilter? filter = null, CancellationToken token = default);

        /// <summary>
        /// Pending approvals whose expiry has passed, across tenants.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="maxResults">Maximum rows. Clamped to 1..1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approvals.</returns>
        Task<List<Approval>> ListExpiredAsync(DateTime nowUtc, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Pending approvals of a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approvals.</returns>
        Task<List<Approval>> ListPendingAsync(string tenantId, CancellationToken token = default);
    }
}
