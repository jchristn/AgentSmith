namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Reads the <c>{"status","data"}</c> envelope shared by the Prometheus and Loki HTTP APIs. Thread-safe.
    /// </summary>
    public class PromApiClient
    {
        #region Public-Members

        /// <summary>
        /// Transport. Never null.
        /// </summary>
        public ObservabilityTransport Transport { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        /// <exception cref="ArgumentNullException">Thrown when transport is null.</exception>
        public PromApiClient(ObservabilityTransport transport)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET an API path and return its data.
        /// </summary>
        /// <typeparam name="T">Data type.</typeparam>
        /// <param name="path">Path.</param>
        /// <param name="query">Query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Envelope with non-null data. A success without a data field (Loki answers labels that way before it
        /// has stored any lines) yields empty data.</returns>
        /// <exception cref="ConnectorException">Thrown when the API reports an error.</exception>
        public async Task<PromResponse<T>> GetAsync<T>(string path, IList<KeyValuePair<string, string?>>? query, CancellationToken token) where T : class, new()
        {
            string body = await Transport.GetAsync(path, query, token).ConfigureAwait(false);
            PromResponse<T> response = ObservabilityJson.DeserializeUpstream<PromResponse<T>>(body, Transport.Provider);
            if (!String.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                string message = String.IsNullOrEmpty(response.Error) ? "unknown error" : response.Error;
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, Transport.Provider + " reported an error" + (String.IsNullOrEmpty(response.ErrorType) ? String.Empty : " (" + response.ErrorType + ")") + ": " + message);
            }
            if (response.Data == null) response.Data = new T();
            return response;
        }

        #endregion
    }
}
