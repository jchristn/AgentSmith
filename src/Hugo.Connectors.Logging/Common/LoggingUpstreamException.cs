namespace Hugo.Connectors.Logging
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A non-success HTTP response from a log service, with the status code and, for rate limiting, the number of
    /// seconds to wait before retrying. Codes: <c>auth_failed</c> (401, 403), <c>rate_limited</c> (429),
    /// <c>not_found</c> (404), <c>invalid_argument</c> (400), <c>upstream_error</c> (anything else).
    /// </summary>
    public class LoggingUpstreamException : ConnectorException
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code returned by the service.
        /// </summary>
        public int StatusCode { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message. Never contains secrets.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="retryAfterSeconds">Seconds to wait before retrying, or null.</param>
        public LoggingUpstreamException(string code, string message, int statusCode, int? retryAfterSeconds)
            : base(code, message)
        {
            StatusCode = statusCode;
            RetryAfterSeconds = retryAfterSeconds;
        }

        #endregion
    }
}
