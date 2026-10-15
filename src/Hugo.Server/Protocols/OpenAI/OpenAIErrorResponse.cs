namespace Hugo.Server.Protocols.OpenAI
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Error body of the OpenAI-compatible routes: <c>{ "error": { ... } }</c>.
    /// </summary>
    public class OpenAIErrorResponse
    {
        /// <summary>
        /// Error.
        /// </summary>
        [JsonPropertyName("error")]
        public OpenAIError Error { get; set; } = new OpenAIError();
    }
}
