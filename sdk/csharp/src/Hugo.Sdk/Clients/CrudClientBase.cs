namespace Hugo.Sdk.Clients
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;

    /// <summary>
    /// Standard list, enumerate, read, create, update, and delete operations for one collection.
    /// </summary>
    /// <typeparam name="TModel">Record type.</typeparam>
    /// <typeparam name="TRequest">Create and update request type.</typeparam>
    public abstract class CrudClientBase<TModel, TRequest> : ReadClientBase<TModel> where TModel : class where TRequest : class
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        /// <param name="segment">Collection segment.</param>
        /// <param name="tenantScoped">True for tenant-scoped paths.</param>
        protected CrudClientBase(HugoTransport transport, string segment, bool tenantScoped) : base(transport, segment, tenantScoped)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a record.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 or 422 with field errors when invalid).</exception>
        public Task<TModel> CreateAsync(TRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<TModel>(Collection(), request, token);
        }

        /// <summary>
        /// Update a record. Null request properties keep their stored values.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<TModel> UpdateAsync(string id, TRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PutAsync<TModel>(Item(id), request, token);
        }

        #endregion
    }
}
