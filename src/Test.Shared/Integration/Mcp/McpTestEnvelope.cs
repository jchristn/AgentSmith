namespace Test.Shared.Integration.Mcp
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A raw JSON-RPC response as the tests read it.
    /// </summary>
    public class McpTestEnvelope
    {
        /// <summary>
        /// Result.
        /// </summary>
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; } = null;

        /// <summary>
        /// Error.
        /// </summary>
        [JsonPropertyName("error")]
        public McpTestError? Error { get; set; } = null;
    }
}
