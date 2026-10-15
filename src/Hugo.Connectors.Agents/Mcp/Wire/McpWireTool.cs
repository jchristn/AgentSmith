namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of one MCP tool definition. Not thread-safe.
    /// </summary>
    public class McpWireTool
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Display title, or null.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description, or null.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// Input schema, passed through without interpretation, or null.
        /// </summary>
        [JsonPropertyName("inputSchema")]
        public JsonElement? InputSchema { get; set; } = null;

        /// <summary>
        /// Behavior hints, or null.
        /// </summary>
        [JsonPropertyName("annotations")]
        public McpWireToolAnnotations? Annotations { get; set; } = null;
    }
}
