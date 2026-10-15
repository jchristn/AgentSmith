namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of the MCP <c>resources/read</c> result. Not thread-safe.
    /// </summary>
    public class McpWireReadResourceResult
    {
        /// <summary>
        /// Contents, or null.
        /// </summary>
        [JsonPropertyName("contents")]
        public List<McpWireResourceContents>? Contents { get; set; } = null;
    }
}
