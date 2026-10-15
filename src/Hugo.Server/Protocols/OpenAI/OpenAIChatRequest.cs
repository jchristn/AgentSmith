namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// <c>POST /v1.0/openai/v1/chat/completions</c> request (the subset Hugo reads). Sampling parameters are accepted and
    /// ignored: the profile's model settings apply. Not thread-safe.
    /// </summary>
    public class OpenAIChatRequest
    {
        /// <summary>
        /// Model: the agent profile name (or identifier).
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Messages. The last one must be a user message.
        /// </summary>
        [JsonPropertyName("messages")]
        public List<OpenAIChatMessage>? Messages { get; set; } = null;

        /// <summary>
        /// Whether to stream chunks over SSE.
        /// </summary>
        [JsonPropertyName("stream")]
        public bool? Stream { get; set; } = null;

        /// <summary>
        /// Stream options.
        /// </summary>
        [JsonPropertyName("stream_options")]
        public OpenAIStreamOptions? StreamOptions { get; set; } = null;

        /// <summary>
        /// Client tools: rejected, because Hugo owns the tools.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<OpenAIToolDefinition>? Tools { get; set; } = null;

        /// <summary>
        /// Legacy client functions: rejected like tools.
        /// </summary>
        [JsonPropertyName("functions")]
        public List<OpenAIToolDefinition>? Functions { get; set; } = null;

        /// <summary>
        /// End-user key; keeps a server-side conversation per caller and key.
        /// </summary>
        [JsonPropertyName("user")]
        public string? User { get; set; } = null;

        /// <summary>
        /// Accepted and ignored.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Accepted and ignored.
        /// </summary>
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; } = null;
    }
}
