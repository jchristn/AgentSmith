namespace Hugo.Connectors.Agents.Mcp.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>mcp_read_resource</c>. Not thread-safe.
    /// </summary>
    public class McpReadPayload
    {
        /// <summary>
        /// Requested URI.
        /// </summary>
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = String.Empty;

        /// <summary>
        /// Server name and version.
        /// </summary>
        [JsonPropertyName("server")]
        public string Server { get; set; } = String.Empty;

        /// <summary>
        /// Contents (text clipped, binary summarized).
        /// </summary>
        [JsonPropertyName("contents")]
        public List<McpContentPayload> Contents { get; set; } = new List<McpContentPayload>();
    }
}
