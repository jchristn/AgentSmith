namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// Error raised by a connector with a stable, machine-readable code (for example <c>connection_failed</c>,
    /// <c>timeout</c>, <c>not_found</c>, <c>permission_denied</c>, <c>invalid_argument</c>). The pipeline converts it to
    /// a tool error result. Messages must never contain secret values.
    /// </summary>
    public class ConnectorException : Exception
    {
        /// <summary>
        /// Stable error code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Seconds the caller should wait before retrying, when known. Null otherwise.
        /// </summary>
        public double? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message.</param>
        public ConnectorException(string code, string message) : base(message)
        {
            Code = String.IsNullOrWhiteSpace(code) ? "connector_error" : code;
        }

        /// <summary>
        /// Instantiate with an inner exception.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception.</param>
        public ConnectorException(string code, string message, Exception inner) : base(message, inner)
        {
            Code = String.IsNullOrWhiteSpace(code) ? "connector_error" : code;
        }
    }
}
