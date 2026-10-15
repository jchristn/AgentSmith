namespace Test.Shared.Integration.Mcp
{
    using System;

    /// <summary>
    /// A raw HTTP answer from /mcp.
    /// </summary>
    public class McpRawResponse
    {
        /// <summary>
        /// HTTP status.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// The JSON-RPC message.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// WWW-Authenticate, or null.
        /// </summary>
        public string? Challenge { get; set; } = null;

        /// <summary>
        /// Mcp-Session-Id, or null.
        /// </summary>
        public string? SessionId { get; set; } = null;
    }
}
