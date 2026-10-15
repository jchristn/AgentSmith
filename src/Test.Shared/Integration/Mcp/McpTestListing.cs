namespace Test.Shared.Integration.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// prompts/list, resources/list, prompts/get, and resources/read results as the tests read them.
    /// </summary>
    public class McpTestListing
    {
        /// <summary>
        /// Prompts (prompts/list).
        /// </summary>
        [JsonPropertyName("prompts")]
        public List<McpTestContent>? Prompts { get; set; } = null;

        /// <summary>
        /// Resources (resources/list; name in Text is not set, uri is).
        /// </summary>
        [JsonPropertyName("resources")]
        public List<McpTestContent>? Resources { get; set; } = null;

        /// <summary>
        /// Contents (resources/read).
        /// </summary>
        [JsonPropertyName("contents")]
        public List<McpTestContent>? Contents { get; set; } = null;

        /// <summary>
        /// Messages (prompts/get).
        /// </summary>
        [JsonPropertyName("messages")]
        public List<McpTestPromptMessage>? Messages { get; set; } = null;
    }
}
