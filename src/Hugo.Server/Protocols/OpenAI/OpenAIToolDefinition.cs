namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A client-supplied tool or tool call. Hugo owns its tools, so the endpoint only detects these to reject them.
    /// </summary>
    public class OpenAIToolDefinition
    {
        /// <summary>
        /// Type, usually function.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Identifier (tool calls).
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;
    }
}
