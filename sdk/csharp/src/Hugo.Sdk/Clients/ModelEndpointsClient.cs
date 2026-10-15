namespace Hugo.Sdk.Clients
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
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
        /// Models the provider offers, where supported.
        /// </summary>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Model names.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
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
