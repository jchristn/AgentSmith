namespace Test.Shared.Integration.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A content block of an MCP result as the tests read it.
    /// </summary>
    public class McpTestContent
    {
        /// <summary>
        /// Content type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Resource URI (resource contents).
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; set; } = null;
    }
}
