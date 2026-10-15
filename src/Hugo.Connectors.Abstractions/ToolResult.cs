namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of one tool call. Text is what the LLM sees; <see cref="StructuredJson"/> is optional machine-readable
    /// content (MCP <c>structuredContent</c>). Neither may contain secret values; the server scrubs both as a backstop.
    /// </summary>
    public class ToolResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the call failed. A failed call is still a normal tool result the LLM can reason about.
        /// </summary>
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Stable error code when <see cref="IsError"/> is true.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Seconds the caller should wait before retrying, when the upstream said so (for example a 429 with
        /// Retry-After). Null when unknown.
        /// </summary>
        public double? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Text content. Never null.
        /// </summary>
        public string Text
        {
            get
            {
                return _Text;
            }
            set
            {
                _Text = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Optional structured content as a JSON object string.
        /// </summary>
        public string? StructuredJson { get; set; } = null;

        /// <summary>
        /// MIME type of <see cref="Text"/>. Default <c>text/plain</c>.
        /// </summary>
        public string MimeType { get; set; } = "text/plain";

        /// <summary>
        /// Whether output was truncated to the limits.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Size in bytes of the output before truncation.
        /// </summary>
        public long OriginalBytes { get; set; } = 0;

        /// <summary>
        /// Numeric facts about the call (for example <c>rows</c>, <c>exitCode</c>, <c>upstreamMs</c>). Never null.
        /// </summary>
        public Dictionary<string, double> Metrics
        {
            get
            {
                return _Metrics;
            }
            set
            {
                _Metrics = value ?? new Dictionary<string, double>(StringComparer.Ordinal);
            }
        }

        #endregion

        #region Private-Members

        private string _Text = String.Empty;
        private Dictionary<string, double> _Metrics = new Dictionary<string, double>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a text result.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Result.</returns>
        public static ToolResult FromText(string text)
        {
            return new ToolResult { Text = text };
        }

        /// <summary>
        /// Build a text result with structured content.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="structuredJson">Structured JSON object.</param>
        /// <returns>Result.</returns>
        public static ToolResult FromText(string text, string? structuredJson)
        {
            return new ToolResult { Text = text, StructuredJson = structuredJson };
        }

        /// <summary>
        /// Build an error result.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message.</param>
        /// <returns>Result.</returns>
        public static ToolResult Error(string code, string message)
        {
            return new ToolResult { IsError = true, ErrorCode = code, Text = message };
        }

        #endregion
    }
}
