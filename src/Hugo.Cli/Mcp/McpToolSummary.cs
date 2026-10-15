namespace Hugo.Cli.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A tool definition, reduced to its name and input schema.
    /// </summary>
    public class McpToolSummary
    {
        /// <summary>
        /// Tool name, or null.
        /// </summary>
        [JsonPropertyName("name")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Input schema, or null.
        /// </summary>
        [JsonPropertyName("inputSchema")]
        public McpToolInputSchema? InputSchema { get; set; } = null;
    }
}
