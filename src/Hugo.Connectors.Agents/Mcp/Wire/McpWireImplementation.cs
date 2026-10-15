namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of an MCP implementation descriptor (<c>serverInfo</c>). Not thread-safe.
    /// </summary>
    public class McpWireImplementation
    {
        /// <summary>
        /// Name, or null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Display title, or null.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Version, or null.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; } = null;
    }
}
