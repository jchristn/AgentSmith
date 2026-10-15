namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Read-only aggregation queries over conversations, messages, tool invocations, and approvals (P6.17, P6.18).
    /// Every method is tenant scoped and bounded by a time window. Thread-safe.
    /// </summary>
    public interface IAnalyticsMethods
    {
        /// <summary>
        /// Token accounting rows of assistant and summary messages created in [from, to).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="fromUtc">Window start (inclusive).</param>
        /// <param name="toUtc">Window end (exclusive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows, oldest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        Task<List<MessageUsageRow>> ListMessageUsageAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default);

        /// <summary>
        /// Tool invocations created in [from, to).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="fromUtc">Window start (inclusive).</param>
        /// <param name="toUtc">Window end (exclusive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows, oldest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        Task<List<ToolUsageRow>> ListToolUsageAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default);

        /// <summary>
        /// Conversations created in [from, to).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="fromUtc">Window start (inclusive).</param>
        /// <param name="toUtc">Window end (exclusive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        Task<long> CountConversationsAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default);

        /// <summary>
        /// Approvals created in [from, to).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="fromUtc">Window start (inclusive).</param>
        /// <param name="toUtc">Window end (exclusive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        Task<long> CountApprovalsAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default);
    }
}
