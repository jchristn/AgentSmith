namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Inference and embedding model endpoints (/v1.0/api/model-endpoints).
    /// </summary>
    public class ModelEndpointsClient : CrudClientBase<ModelEndpoint, ModelEndpointRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public ModelEndpointsClient(HugoTransport transport) : base(transport, "model-endpoints", false)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List one page of model endpoints with filters. Unset filter properties are not sent.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="query">Optional paging, ordering, and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 ValidationFailed listing the allowed values when kind, provider, or health is unknown).</exception>
        public Task<EnumerationResult<ModelEndpoint>> ListAsync(ModelEndpointFilter filter, EnumerationQuery? query = null, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            Dictionary<string, string?> filters = new Dictionary<string, string?>
            {
                { "kind", filter.Kind },
                { "provider", filter.Provider },
                { "health", filter.Health }
            };
            return ListAsync(query, filters, token);
        }

        /// <summary>
        /// Send a tiny prompt (or embed a short string) and record latency, dimensions, and health.
        /// </summary>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validation result.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ModelValidationResult> ValidateAsync(string id, CancellationToken token = default)
        {
            return PostAsync<ModelValidationResult>(Item(id) + "/validate", null, token);
        }

        /// <summary>
        /// Validate unsaved endpoint settings (the same body as create) with a tiny prompt or embedding. Nothing is
        /// stored and no health is recorded; failures to reach the provider come back as Success false with Error.
        /// </summary>
        /// <param name="request">Draft settings; Provider is required.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validation result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 when the draft is invalid, for example without a provider).</exception>
        public Task<ModelValidationResult> ValidateDraftAsync(ModelEndpointRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<ModelValidationResult>("/v1.0/api/model-endpoints/validate", request, token);
        }

        /// <summary>
        /// Models the provider offers, where supported.
        /// </summary>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Model names.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status (502 ProviderUnreachable or ProviderTimeout when the provider cannot be reached).</exception>
        public Task<List<string>> ListModelsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<string>>(Item(id) + "/models", null, token);
        }

        /// <summary>
        /// Recent health checks of an endpoint, newest first.
        /// </summary>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="maxResults">Most records, 1 to 1000, or null for the server default (50).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health records.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<ModelEndpointHealthRecord>> GetHealthHistoryAsync(string id, int? maxResults = null, CancellationToken token = default)
        {
            Dictionary<string, string?>? query = maxResults.HasValue ? new Dictionary<string, string?> { { "maxResults", maxResults.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) } } : null;
            return GetAsync<List<ModelEndpointHealthRecord>>(Item(id) + "/health-history", query, token);
        }

        #endregion
    }
}
