namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of MCP resource contents (text or blob). Not thread-safe.
    /// </summary>
    public class McpWireResourceContents
    {
        /// <summary>
        /// Resource URI, or null.
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; set; } = null;

        /// <summary>
        /// MIME type, or null.
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; } = null;

        /// <summary>
        /// Text contents, or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Base64 binary contents, or null.
        /// </summary>
        [JsonPropertyName("blob")]
        public string? Blob { get; set; } = null;
    }
}
