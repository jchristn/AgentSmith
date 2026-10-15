namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A model entry of <c>GET /v1.0/openai/v1/models</c>: one agent profile the caller can use.
    /// </summary>
    public class OpenAIModel
    {
        /// <summary>
        /// Profile name (use as <c>model</c>).
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Always model.
        /// </summary>
        [JsonPropertyName("object")]
        public string Object { get; set; } = "model";

        /// <summary>
        /// Profile creation time (Unix seconds).
        /// </summary>
        [JsonPropertyName("created")]
        public long Created { get; set; } = 0;

        /// <summary>
        /// Always hugo.
        /// </summary>
        [JsonPropertyName("owned_by")]
        public string OwnedBy { get; set; } = "hugo";
    }
}
