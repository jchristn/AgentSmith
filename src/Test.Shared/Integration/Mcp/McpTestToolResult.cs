namespace Test.Shared.Integration.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A tools/call result as the tests read it.
    /// </summary>
    public class McpTestToolResult
    {
        /// <summary>
        /// Content blocks.
        /// </summary>
        [JsonPropertyName("content")]
        public List<McpTestContent> Content { get; set; } = new List<McpTestContent>();

        /// <summary>
        /// Structured content.
        /// </summary>
        [JsonPropertyName("structuredContent")]
        public JsonElement? StructuredContent { get; set; } = null;

        /// <summary>
        /// Whether the result is an error.
        /// </summary>
        [JsonPropertyName("isError")]
        public bool? IsError { get; set; } = null;

        /// <summary>
        /// All text content joined.
        /// </summary>
        [JsonIgnore]
        public string Text
        {
            get
            {
                return String.Join("\n", Content.Where(c => c.Text != null).Select(c => c.Text));
            }
        }

        /// <summary>
        /// Structured content as raw JSON, or an empty string.
        /// </summary>
        [JsonIgnore]
        public string StructuredJson
        {
            get
            {
                return StructuredContent.HasValue ? StructuredContent.Value.GetRawText() : String.Empty;
            }
        }
    }
}
