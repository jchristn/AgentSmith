namespace Test.Shared.Agent.StubLlm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAI chat completions request as received by the stub.
    /// </summary>
    public class StubChatRequest
    {
        /// <summary>
        /// Model.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Messages.
        /// </summary>
        [JsonPropertyName("messages")]
        public List<StubChatMessage>? Messages { get; set; } = null;

        /// <summary>
        /// Tools.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<StubToolWire>? Tools { get; set; } = null;

        /// <summary>
        /// Tool choice; only the string form is supported.
        /// </summary>
        [JsonPropertyName("tool_choice")]
        public string? ToolChoice { get; set; } = null;

        /// <summary>
        /// Whether to stream.
        /// </summary>
        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;
    }
}
