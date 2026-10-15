namespace Test.Shared.Agent.StubLlm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAI chat message as received by the stub.
    /// </summary>
    public class StubChatMessage
    {
        /// <summary>
        /// Role.
        /// </summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        /// <summary>
        /// Content.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; } = null;

        /// <summary>
        /// Tool call ID (tool messages).
        /// </summary>
        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Tool calls (assistant messages).
        /// </summary>
        [JsonPropertyName("tool_calls")]
        public List<StubToolCallWire>? ToolCalls { get; set; } = null;
    }
}
