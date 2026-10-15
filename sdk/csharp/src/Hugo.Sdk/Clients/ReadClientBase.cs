namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;

    /// <summary>
    /// List, enumerate, read, and delete operations for one collection, either tenant-scoped
    /// (/v1.0/api/tenants/{tenantId}/{segment}) or flat with the tenant taken from the credentials (/v1.0/api/{segment}).
    /// </summary>
    /// <typeparam name="TModel">Record type.</typeparam>
    public abstract class ReadClientBase<TModel> : ClientBase where TModel : class
    {
        #region Private-Members

        private readonly string _Segment;
        private readonly bool _TenantScoped;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        /// <param name="segment">Collection segment, for example users or agent-profiles.</param>
        /// <param name="tenantScoped">True for /v1.0/api/tenants/{tenantId}/{segment}; false for /v1.0/api/{segment}.</param>
        /// <exception cref="ArgumentNullException">Thrown when segment is null or empty.</exception>
        protected ReadClientBase(HugoTransport transport, string segment, bool tenantScoped) : base(transport)
        {
            if (String.IsNullOrEmpty(segment)) throw new ArgumentNullException(nameof(segment));
            _Segment = segment;
            _TenantScoped = tenantScoped;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List one page of records.
        /// </summary>
        /// <param name="query">Optional paging, ordering, and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EnumerationResult<TModel>> ListAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return ListAsync(query, null, token);
        }

        /// <summary>
        /// Enumerate every record, fetching pages of query.MaxResults until the server reports the end.
        /// </summary>
        /// <param name="query">Optional paging start, ordering, and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async IAsyncEnumerable<TModel> EnumerateAsync(EnumerationQuery? query = null, [EnumeratorCancellation] CancellationToken token = default)
        {
            EnumerationQuery page = query ?? new EnumerationQuery();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<TModel> result = await ListAsync(page, null, token).ConfigureAwait(false);
                foreach (TModel item in result.Objects) yield return item;
                if (result.EndOfResults || result.Objects.Count == 0) yield break;
                EnumerationQuery next = new EnumerationQuery();
                next.MaxResults = page.MaxResults;
                next.Skip = page.Skip + result.Objects.Count;
                next.Ordering = page.Ordering;
                next.Search = page.Search;
                next.ActiveOnly = page.ActiveOnly;
                next.IncludeBuiltIn = page.IncludeBuiltIn;
                page = next;
            }
        }

        /// <summary>
        /// Read a record.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status (404 when it does not exist).</exception>
        public Task<TModel> GetAsync(string id, CancellationToken token = default)
        {
            return GetAsync<TModel>(Item(id), null, token);
        }

        /// <summary>
        /// Delete a record.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteAsync(Item(id), null, token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// List one page with extra filter parameters.
        /// </summary>
        /// <param name="query">Optional paging.</param>
        /// <param name="filters">Extra query parameters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        protected async Task<EnumerationResult<TModel>> ListAsync(EnumerationQuery? query, IDictionary<string, string?>? filters, CancellationToken token)
        {
            Dictionary<string, string?> parameters = QueryBuilder.From(query);
            if (filters != null) foreach (KeyValuePair<string, string?> pair in filters) parameters[pair.Key] = pair.Value;
            EnumerationResult<TModel>? result = await Transport.SendAsync<EnumerationResult<TModel>>(System.Net.Http.HttpMethod.Get, Collection(), null, parameters, token).ConfigureAwait(false);
            return result ?? new EnumerationResult<TModel>();
        }

        /// <summary>
        /// Collection path.
        /// </summary>
        /// <returns>Path.</returns>
        protected string Collection()
        {
            return _TenantScoped ? TenantPath("/" + _Segment) : "/v1.0/api/" + _Segment;
        }

        /// <summary>
        /// Item path.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <returns>Path.</returns>
        protected string Item(string id)
        {
            return Collection() + "/" + Segment(id);
        }

        #endregion
    }
}
