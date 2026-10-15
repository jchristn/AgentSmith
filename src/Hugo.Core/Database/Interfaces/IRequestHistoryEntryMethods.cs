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
    /// RequestHistoryEntry data access methods.
    /// </summary>
    public interface IRequestHistoryEntryMethods
    {
        /// <summary>
        /// Create a RequestHistoryEntry, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<RequestHistoryEntry> CreateAsync(RequestHistoryEntry obj, CancellationToken token = default);

        /// <summary>
        /// Read a RequestHistoryEntry by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a RequestHistoryEntry by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<RequestHistoryEntry?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a RequestHistoryEntry, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<RequestHistoryEntry> UpdateAsync(RequestHistoryEntry obj, CancellationToken token = default);

        /// <summary>
        /// Delete a RequestHistoryEntry and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a RequestHistoryEntry exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate RequestHistoryEntry records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Page through entries matching a filter. Bodies and headers are omitted.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<RequestHistoryPage> EnumerateAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Return time-bucketed counts and averages between FromUtc and ToUtc. Every bucket is present, including empty ones.
        /// </summary>
        /// <param name="filter">Filter; FromUtc and ToUtc default to the last 24 hours.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete all entries matching a filter.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete entries created before a cutoff.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default);
    }
}
