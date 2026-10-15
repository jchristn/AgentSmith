namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of MCP server capabilities. Each member is present (non-null) when the server declares that capability. Not thread-safe.
    /// </summary>
    public class McpWireServerCapabilities
    {
        /// <summary>
        /// Present when the server offers tools.
        /// </summary>
        [JsonPropertyName("tools")]
        public object? Tools { get; set; } = null;

        /// <summary>
        /// Present when the server offers resources.
        /// </summary>
        [JsonPropertyName("resources")]
        public object? Resources { get; set; } = null;

        /// <summary>
        /// Present when the server offers prompts.
        /// </summary>
        [JsonPropertyName("prompts")]
        public object? Prompts { get; set; } = null;
    }
}
