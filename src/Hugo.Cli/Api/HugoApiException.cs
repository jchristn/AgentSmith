namespace Hugo.Cli.Api
{
    using System;

    /// <summary>
    /// Thrown when the Hugo REST API answers with a status other than success.
    /// </summary>
    public class HugoApiException : Exception
    {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Stable error code from the body (for example <c>PasswordChangeRequired</c>), or null.
        /// </summary>
        public string? Code { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="code">Error code from the body, or null.</param>
        /// <param name="message">Message.</param>
        public HugoApiException(int statusCode, string? code, string message) : base(message)
        {
            StatusCode = statusCode;
            Code = code;
        }
    }
}
