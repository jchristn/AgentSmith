namespace Hugo.Connectors.Observability
{
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A non-success HTTP response from an observability backend, mapped to a stable error code.
    /// </summary>
    public class ObservabilityUpstreamException : ConnectorException
    {
        /// <summary>
        /// HTTP status code returned by the backend.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message without secrets.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="retryAfterSeconds">Seconds to wait before retrying, or null.</param>
        public ObservabilityUpstreamException(string code, string message, int statusCode, double? retryAfterSeconds) : base(code, message)
        {
            StatusCode = statusCode;
            RetryAfterSeconds = retryAfterSeconds;
        }
    }
}
