namespace Test.Shared.Integration.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One property of a tool input schema as the tests read it.
    /// </summary>
    public class McpTestSchemaProperty
    {
        /// <summary>
        /// Allowed values, or null.
        /// </summary>
        [JsonPropertyName("enum")]
        public List<string>? Enum { get; set; } = null;
    }
}
