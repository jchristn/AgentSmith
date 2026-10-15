namespace Hugo.Server.Protocols.OpenAI
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Token usage of a chat completion (every model call of the turn, tools included).
    /// </summary>
    public class OpenAIUsage
    {
        /// <summary>
        /// Input tokens.
        /// </summary>
        [JsonPropertyName("prompt_tokens")]
        public long PromptTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        [JsonPropertyName("completion_tokens")]
        public long CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Sum of input and output tokens.
        /// </summary>
        [JsonPropertyName("total_tokens")]
        public long TotalTokens { get; set; } = 0;
    }
}
