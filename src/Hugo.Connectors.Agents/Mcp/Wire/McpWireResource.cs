namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of one MCP resource descriptor. Not thread-safe.
    /// </summary>
    public class McpWireResource
    {
        /// <summary>
        /// URI.
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; set; } = null;

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
        /// Description, or null.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// MIME type, or null.
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; } = null;

        /// <summary>
        /// Size in bytes, or null.
        /// </summary>
        [JsonPropertyName("size")]
        public long? Size { get; set; } = null;
    }
}
