namespace Hugo.Connectors.Agents.Mcp.Payloads
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One remote resource in <c>mcp_list_resources</c> output. Not thread-safe.
    /// </summary>
    public class McpResourcePayload
    {
        /// <summary>
        /// URI; pass it to mcp_read_resource.
        /// </summary>
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = String.Empty;

        /// <summary>
        /// Name, or null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Title, or null.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description (clipped), or null.
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
