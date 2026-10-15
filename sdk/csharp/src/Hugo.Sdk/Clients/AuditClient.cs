namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Audit records in the client's tenant (/v1.0/api/tenants/{tenantId}/audit).
    /// </summary>
    public class AuditClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public AuditClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List audit records, newest first.
        /// </summary>
        /// <param name="filter">Optional filter and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PageResult<AuditRecord>> ListAsync(AuditFilter? filter = null, CancellationToken token = default)
        {
            return GetAsync<PageResult<AuditRecord>>(TenantPath("/audit"), Query(filter), token);
        }

        /// <summary>
        /// Read an audit record.
        /// </summary>
        /// <param name="id">Record identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<AuditRecord> GetAsync(string id, CancellationToken token = default)
        {
            return GetAsync<AuditRecord>(TenantPath("/audit/" + Segment(id)), null, token);
        }

        /// <summary>
        /// Delete an audit record.
        /// </summary>
        /// <param name="id">Record identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteAsync(TenantPath("/audit/" + Segment(id)), null, token);
        }

        /// <summary>
        /// Delete every audit record matching a filter.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of deleted records.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<DeleteCountResponse> DeleteManyAsync(AuditFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return Transport.SendRequiredAsync<DeleteCountResponse>(HttpMethod.Delete, TenantPath("/audit"), null, Query(filter), token);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string?> Query(AuditFilter? filter)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (filter == null) return query;
            query["eventType"] = filter.EventType;
            query["principalId"] = filter.PrincipalId;
            query["result"] = filter.Result;
            query["fromUtc"] = QueryBuilder.Date(filter.FromUtc);
            query["toUtc"] = QueryBuilder.Date(filter.ToUtc);
            query["pageNumber"] = QueryBuilder.Int(filter.PageNumber);
            query["pageSize"] = QueryBuilder.Int(filter.PageSize);
            return query;
        }

        #endregion
    }
}
