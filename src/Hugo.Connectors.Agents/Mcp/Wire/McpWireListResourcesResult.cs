namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of the MCP <c>resources/list</c> result. Not thread-safe.
    /// </summary>
    public class McpWireListResourcesResult
    {
        /// <summary>
        /// Resources, or null.
        /// </summary>
        [JsonPropertyName("resources")]
        public List<McpWireResource>? Resources { get; set; } = null;

        /// <summary>
        /// Cursor for the next page, or null on the last page.
        /// </summary>
        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; set; } = null;
    }
}
