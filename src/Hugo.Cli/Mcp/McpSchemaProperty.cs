namespace Hugo.Cli.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One property of a tool input schema: its type and its <c>x-mcp-header</c> annotation.
    /// </summary>
    public class McpSchemaProperty
    {
        /// <summary>
        /// JSON Schema type when it is a single string (<c>string</c>, <c>integer</c>, <c>boolean</c>, ...), or null.
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Type { get; set; } = null;

        /// <summary>
        /// The <c>x-mcp-header</c> annotation (the header name portion after <c>Mcp-Param-</c>), or null.
        /// </summary>
        [JsonPropertyName("x-mcp-header")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Header { get; set; } = null;
    }
}
