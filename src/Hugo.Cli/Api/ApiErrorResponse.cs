namespace Hugo.Cli.Api
{
    /// <summary>
    /// Error body of the Hugo REST API, for example <c>{"error":"Forbidden","code":"PasswordChangeRequired","message":"..."}</c>.
    /// </summary>
    public class ApiErrorResponse
    {
        /// <summary>
        /// Error name derived from the status code, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Stable error code, or null.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Human-readable message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Request ID for support, or null.
        /// </summary>
        public string? RequestId { get; set; } = null;
    }
}
