namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Captured HTTP requests (/v1.0/api/request-history). Tenant users see their tenant; system administrators can
    /// filter by tenant and user.
    /// </summary>
    public class RequestHistoryClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public RequestHistoryClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List captured requests, newest first. Bodies and headers are included only by GetAsync.
        /// </summary>
        /// <param name="filter">Optional filter and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PageResult<RequestHistoryEntry>> ListAsync(RequestHistoryFilter? filter = null, CancellationToken token = default)
        {
            return GetAsync<PageResult<RequestHistoryEntry>>("/v1.0/api/request-history", Query(filter), token);
        }

        /// <summary>
        /// Time-bucketed request counts.
        /// </summary>
        /// <param name="fromUtc">Start, UTC, or null for the server default.</param>
        /// <param name="toUtc">End, UTC, or null for now.</param>
        /// <param name="bucketMinutes">Bucket width in minutes, or null for the server default (15).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<RequestHistorySummary> GetSummaryAsync(DateTime? fromUtc = null, DateTime? toUtc = null, int? bucketMinutes = null, CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?>(StringComparer.Ordinal);
            query["fromUtc"] = QueryBuilder.Date(fromUtc);
            query["toUtc"] = QueryBuilder.Date(toUtc);
            query["bucketMinutes"] = bucketMinutes.HasValue ? bucketMinutes.Value.ToString(CultureInfo.InvariantCulture) : null;
            return GetAsync<RequestHistorySummary>("/v1.0/api/request-history/summary", query, token);
        }

        /// <summary>
        /// Read a captured request with headers and bodies.
        /// </summary>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<RequestHistoryEntry> GetAsync(string id, CancellationToken token = default)
        {
            return GetAsync<RequestHistoryEntry>("/v1.0/api/request-history/" + Segment(id), null, token);
        }

        /// <summary>
        /// Delete a captured request.
        /// </summary>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteAsync("/v1.0/api/request-history/" + Segment(id), null, token);
        }

        /// <summary>
        /// Delete every captured request matching a filter.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of deleted entries.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<DeleteCountResponse> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return Transport.SendRequiredAsync<DeleteCountResponse>(HttpMethod.Delete, "/v1.0/api/request-history", null, Query(filter), token);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string?> Query(RequestHistoryFilter? filter)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (filter == null) return query;
            query["method"] = filter.Method;
            query["statusCode"] = QueryBuilder.Int(filter.StatusCode);
            query["pathContains"] = filter.PathContains;
            query["fromUtc"] = QueryBuilder.Date(filter.FromUtc);
            query["toUtc"] = QueryBuilder.Date(filter.ToUtc);
            query["pageNumber"] = QueryBuilder.Int(filter.PageNumber);
            query["pageSize"] = QueryBuilder.Int(filter.PageSize);
            query["tenantId"] = filter.TenantId;
            query["userId"] = filter.UserId;
            return query;
        }

        #endregion
    }
}
