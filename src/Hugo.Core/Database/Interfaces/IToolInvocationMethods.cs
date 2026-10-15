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
    using Hugo.Core.Responses;

    /// <summary>
    /// Tool invocation data access. Every read is tenant scoped. Implementations must be thread-safe.
    /// </summary>
    public interface IToolInvocationMethods
    {
        /// <summary>
        /// Insert a tool invocation.
        /// </summary>
        /// <param name="obj">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<ToolInvocation> CreateAsync(ToolInvocation obj, CancellationToken token = default);

        /// <summary>
        /// Update a tool invocation.
        /// </summary>
        /// <param name="obj">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        Task<ToolInvocation> UpdateAsync(ToolInvocation obj, CancellationToken token = default);

        /// <summary>
        /// Read a tool invocation.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record or null.</returns>
        Task<ToolInvocation?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Atomically move an invocation from one status to another. Used to claim a call for execution so it runs
        /// exactly once.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="from">Expected current status.</param>
        /// <param name="to">New status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this caller made the transition.</returns>
        Task<bool> TryTransitionAsync(string tenantId, string id, InvocationStatusEnum from, InvocationStatusEnum to, CancellationToken token = default);

        /// <summary>
        /// Enumerate tool invocations, newest first by default.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Paging and ordering; search matches the tool name.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<ToolInvocation>> EnumerateAsync(string tenantId, EnumerationQuery query, ToolInvocationFilter? filter = null, CancellationToken token = default);

        /// <summary>
        /// Counts by decision, status, effect, and tool.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="sinceUtc">Only records at or after this time, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        Task<ToolInvocationSummary> SummaryAsync(string tenantId, DateTime? sinceUtc, CancellationToken token = default);
    }
}
