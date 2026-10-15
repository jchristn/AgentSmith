namespace Hugo.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Output of a connector tool call.
    /// </summary>
    public class ToolResult
    {
        #region Public-Members

        /// <summary>
        /// Text output (scrubbed and truncated to limits).
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Is error.
        /// </summary>
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Error code.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Retry after seconds.
        /// </summary>
        public double? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Structured JSON.
        /// </summary>
        public string? StructuredJson { get; set; } = null;

        /// <summary>
        /// Mime type.
        /// </summary>
        public string? MimeType { get; set; } = null;

        /// <summary>
        /// Truncated.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Original bytes.
        /// </summary>
        public long OriginalBytes { get; set; } = 0;

        /// <summary>
        /// Metrics.
        /// </summary>
        public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();

        #endregion
    }
}
