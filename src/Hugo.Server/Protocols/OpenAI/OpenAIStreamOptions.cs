namespace Hugo.Server.Protocols.OpenAI
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// <c>stream_options</c> of a chat completions request.
    /// </summary>
    public class OpenAIStreamOptions
    {
        /// <summary>
        /// Whether to send a final chunk with usage and no choices.
        /// </summary>
        [JsonPropertyName("include_usage")]
        public bool IncludeUsage { get; set; } = false;
    }
}
