namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of the MCP <c>tools/list</c> result. Not thread-safe.
    /// </summary>
    public class McpWireListToolsResult
    {
        /// <summary>
        /// Tools, or null.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<McpWireTool>? Tools { get; set; } = null;

        /// <summary>
        /// Cursor for the next page, or null on the last page.
        /// </summary>
        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; set; } = null;
    }
}
