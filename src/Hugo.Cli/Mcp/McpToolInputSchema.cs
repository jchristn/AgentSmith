namespace Hugo.Cli.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A tool input schema, reduced to its top-level properties.
    /// </summary>
    public class McpToolInputSchema
    {
        /// <summary>
        /// Top-level properties by name, or null.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, McpSchemaProperty>? Properties { get; set; } = null;
    }
}
