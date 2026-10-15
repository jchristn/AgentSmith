namespace Test.Shared.Integration.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A listed tool as the tests read it.
    /// </summary>
    public class McpTestTool
    {
        /// <summary>
        /// Name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Input schema.
        /// </summary>
        [JsonPropertyName("inputSchema")]
        public McpTestSchema InputSchema { get; set; } = new McpTestSchema();

        /// <summary>
        /// The resource enum of the schema, or an empty list.
        /// </summary>
        [JsonIgnore]
        public List<string> ResourceEnum
        {
            get
            {
                if (InputSchema.Properties != null && InputSchema.Properties.TryGetValue("resource", out McpTestSchemaProperty? resource) && resource.Enum != null) return resource.Enum;
                return new List<string>();
            }
        }
    }
}
