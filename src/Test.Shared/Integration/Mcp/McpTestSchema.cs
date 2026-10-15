namespace Test.Shared.Integration.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A tool input schema as the tests read it.
    /// </summary>
    public class McpTestSchema
    {
        /// <summary>
        /// Properties.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, McpTestSchemaProperty>? Properties { get; set; } = null;

        /// <summary>
        /// Required properties.
        /// </summary>
        [JsonPropertyName("required")]
        public List<string>? Required { get; set; } = null;
    }
}
