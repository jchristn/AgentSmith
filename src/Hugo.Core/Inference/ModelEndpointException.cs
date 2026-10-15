namespace Hugo.Core.Inference
{
    using System;

    /// <summary>
    /// A model endpoint call failed after the gateway applied retries, the circuit breaker, and any fallback.
    /// The message never contains the API key.
    /// </summary>
    public class ModelEndpointException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Stable failure code: <c>http_error</c>, <c>transport_error</c>, <c>timeout</c>, <c>circuit_open</c>,
        /// <c>queue_timeout</c>, <c>stream_interrupted</c>, <c>bad_response</c>, <c>configuration</c>.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Key of the endpoint that failed last.
        /// </summary>
        public string EndpointKey { get; }

        /// <summary>
        /// HTTP status of the last response, when one arrived.
        /// </summary>
        public int? StatusCode { get; }

        /// <summary>
        /// Whether the failure is of a retryable class (429, 5xx, transport, timeout).
        /// </summary>
        public bool Retryable { get; }

        /// <summary>
        /// Retry-After advertised by the endpoint, in seconds, when known.
        /// </summary>
        public double? RetryAfterSeconds { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Failure code.</param>
        /// <param name="endpointKey">Endpoint key.</param>
        /// <param name="message">Message without secrets.</param>
        /// <param name="statusCode">HTTP status, if any.</param>
        /// <param name="retryable">Whether retryable.</param>
        /// <param name="retryAfterSeconds">Retry-After, if any.</param>
        /// <param name="inner">Inner exception, if any.</param>
        public ModelEndpointException(string code, string endpointKey, string message, int? statusCode = null, bool retryable = false, double? retryAfterSeconds = null, Exception? inner = null)
            : base(message, inner)
        {
            Code = code ?? "error";
            EndpointKey = endpointKey ?? String.Empty;
            StatusCode = statusCode;
            Retryable = retryable;
            RetryAfterSeconds = retryAfterSeconds;
        }

        #endregion
    }
}
