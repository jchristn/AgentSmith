namespace Hugo.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a system Hugo calls on the caller's behalf (a model provider, for example) cannot be reached or answers
    /// with an error; mapped to 502 Bad Gateway, or 503 Service Unavailable when the upstream is temporarily unavailable.
    /// The message is shown to the caller, so it must already be scrubbed.
    /// </summary>
    public class UpstreamException : Exception
    {
        /// <summary>
        /// Stable error code, for example ProviderUnreachable.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// HTTP status: 502 (default) or 503. Any other value is treated as 502.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Stable error code; null means UpstreamError.</param>
        /// <param name="message">Scrubbed message.</param>
        /// <param name="statusCode">HTTP status: 502 (default) or 503.</param>
        /// <param name="inner">Underlying exception, or null.</param>
        public UpstreamException(string? code, string message, int statusCode = 502, Exception? inner = null) : base(message, inner)
        {
            Code = code ?? "UpstreamError";
            StatusCode = statusCode == 503 ? 503 : 502;
        }
    }
}
