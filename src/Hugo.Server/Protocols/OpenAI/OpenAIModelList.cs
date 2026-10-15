namespace Hugo.Server.Protocols.OpenAI
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of <c>GET /v1.0/openai/v1/models</c>.
    /// </summary>
    public class OpenAIModelList
    {
        /// <summary>
        /// Always list.
        /// </summary>
        [JsonPropertyName("object")]
        public string Object { get; set; } = "list";

        /// <summary>
        /// Models.
        /// </summary>
        [JsonPropertyName("data")]
        public List<OpenAIModel> Data { get; set; } = new List<OpenAIModel>();
    }
}
