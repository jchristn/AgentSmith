namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of the MCP <c>tools/call</c> result. Not thread-safe.
    /// </summary>
    public class McpWireCallToolResult
    {
        /// <summary>
        /// Content blocks, or null.
        /// </summary>
        [JsonPropertyName("content")]
        public List<McpWireContent>? Content { get; set; } = null;

        /// <summary>
        /// Structured content, passed through without interpretation, or null.
        /// </summary>
        [JsonPropertyName("structuredContent")]
        public JsonElement? StructuredContent { get; set; } = null;

        /// <summary>
        /// Whether the tool reported an error, or null.
        /// </summary>
        [JsonPropertyName("isError")]
        public bool? IsError { get; set; } = null;
    }
}
