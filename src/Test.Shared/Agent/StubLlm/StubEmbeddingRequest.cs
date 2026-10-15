namespace Test.Shared.Agent.StubLlm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAI embeddings request as received by the stub. <c>input</c> may be a string or an array of strings.
    /// </summary>
    public class StubEmbeddingRequest
    {
        /// <summary>
        /// Model.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Inputs.
        /// </summary>
        [JsonPropertyName("input")]
        [JsonConverter(typeof(StubEmbeddingInputConverter))]
        public List<string> Input { get; set; } = new List<string>();
    }
}
