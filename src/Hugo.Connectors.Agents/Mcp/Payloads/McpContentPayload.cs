namespace Hugo.Connectors.Agents.Mcp.Payloads
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One content block of a remote tool result or resource. Binary data is summarized, never included. Not thread-safe.
    /// </summary>
    public class McpContentPayload
    {
        /// <summary>
        /// Block type (text, image, audio, resource_link, resource, blob).
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Text (clipped), or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// MIME type, or null.
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; } = null;

        /// <summary>
        /// URI, or null.
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; set; } = null;

        /// <summary>
        /// Approximate decoded size of omitted binary data, or null.
        /// </summary>
        [JsonPropertyName("bytes")]
        public long? Bytes { get; set; } = null;
    }
}
