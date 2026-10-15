namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of one MCP content block (text, image, audio, resource link, or embedded resource). Not thread-safe.
    /// </summary>
    public class McpWireContent
    {
        /// <summary>
        /// Block type: <c>text</c>, <c>image</c>, <c>audio</c>, <c>resource_link</c>, or <c>resource</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text of a text block, or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Base64 data of an image or audio block, or null.
        /// </summary>
        [JsonPropertyName("data")]
        public string? Data { get; set; } = null;

        /// <summary>
        /// MIME type, or null.
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; } = null;

        /// <summary>
        /// URI of a resource link, or null.
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; set; } = null;

        /// <summary>
        /// Name of a resource link, or null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description of a resource link, or null.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// Embedded resource, or null.
        /// </summary>
        [JsonPropertyName("resource")]
        public McpWireResourceContents? Resource { get; set; } = null;
    }
}
