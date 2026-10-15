namespace Hugo.Server.Protocols.OpenAI
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A choice of a chat completion (with <see cref="Message"/>) or of a stream chunk (with <see cref="Delta"/>).
    /// </summary>
    public class OpenAIChoice
    {
        /// <summary>
        /// Index (always 0).
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; } = 0;

        /// <summary>
        /// The answer (non-streaming).
        /// </summary>
        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAIChatMessage? Message { get; set; } = null;

        /// <summary>
        /// The increment (streaming).
        /// </summary>
        [JsonPropertyName("delta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAIChatMessage? Delta { get; set; } = null;

        /// <summary>
        /// stop or length; null on chunks before the last.
        /// </summary>
        [JsonPropertyName("finish_reason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? FinishReason { get; set; } = null;
    }
}
