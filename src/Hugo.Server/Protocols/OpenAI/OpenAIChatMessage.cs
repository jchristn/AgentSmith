namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A message of an OpenAI chat completions request or response. Not thread-safe.
    /// </summary>
    public class OpenAIChatMessage
    {
        /// <summary>
        /// Role: system, developer, user, assistant, or tool.
        /// </summary>
        [JsonPropertyName("role")]
        public string? Role { get; set; } = null;

        /// <summary>
        /// Text content (a string or text parts on input).
        /// </summary>
        [JsonPropertyName("content")]
        [JsonConverter(typeof(OpenAIContentConverter))]
        public string? Content { get; set; } = null;

        /// <summary>
        /// Optional participant name.
        /// </summary>
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Tool call identifier of a tool message (client tools are not supported).
        /// </summary>
        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Tool calls of an assistant message (client tools are not supported).
        /// </summary>
        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OpenAIToolDefinition>? ToolCalls { get; set; } = null;
    }
}
