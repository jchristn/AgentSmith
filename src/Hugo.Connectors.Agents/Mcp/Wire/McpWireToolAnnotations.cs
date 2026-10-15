namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of MCP tool annotations (behavior hints). Not thread-safe.
    /// </summary>
    public class McpWireToolAnnotations
    {
        /// <summary>
        /// Display title, or null.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Whether the tool does not modify its environment, or null.
        /// </summary>
        [JsonPropertyName("readOnlyHint")]
        public bool? ReadOnlyHint { get; set; } = null;

        /// <summary>
        /// Whether the tool may perform destructive updates, or null.
        /// </summary>
        [JsonPropertyName("destructiveHint")]
        public bool? DestructiveHint { get; set; } = null;

        /// <summary>
        /// Whether repeated calls with the same arguments have no additional effect, or null.
        /// </summary>
        [JsonPropertyName("idempotentHint")]
        public bool? IdempotentHint { get; set; } = null;

        /// <summary>
        /// Whether the tool interacts with an open world of external entities, or null.
        /// </summary>
        [JsonPropertyName("openWorldHint")]
        public bool? OpenWorldHint { get; set; } = null;
    }
}
