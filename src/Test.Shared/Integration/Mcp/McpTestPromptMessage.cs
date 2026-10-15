namespace Test.Shared.Integration.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A prompt message as the tests read it.
    /// </summary>
    public class McpTestPromptMessage
    {
        /// <summary>
        /// Role.
        /// </summary>
        [JsonPropertyName("role")]
        public string? Role { get; set; } = null;

        /// <summary>
        /// Content.
        /// </summary>
        [JsonPropertyName("content")]
        public McpTestContent? Content { get; set; } = null;
    }
}
