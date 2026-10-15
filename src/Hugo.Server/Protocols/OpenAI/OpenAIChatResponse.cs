namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A chat completion (<c>object</c> chat.completion) or one stream chunk (<c>object</c> chat.completion.chunk).
    /// </summary>
    public class OpenAIChatResponse
    {
        /// <summary>
        /// Completion identifier (chatcmpl- plus the turn identifier); the same on every chunk of a stream.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// chat.completion or chat.completion.chunk.
        /// </summary>
        [JsonPropertyName("object")]
        public string Object { get; set; } = "chat.completion";

        /// <summary>
        /// Creation time (Unix seconds).
        /// </summary>
        [JsonPropertyName("created")]
        public long Created { get; set; } = 0;

        /// <summary>
        /// Model: the profile name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Choices (one; none on the usage chunk).
        /// </summary>
        [JsonPropertyName("choices")]
        public List<OpenAIChoice> Choices { get; set; } = new List<OpenAIChoice>();

        /// <summary>
        /// Usage (non-streaming, and the final chunk when stream_options.include_usage is true).
        /// </summary>
        [JsonPropertyName("usage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAIUsage? Usage { get; set; } = null;

        /// <summary>
        /// Hugo extension (non-streaming and the last chunk).
        /// </summary>
        [JsonPropertyName("hugo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAIHugoInfo? Hugo { get; set; } = null;
    }
}
