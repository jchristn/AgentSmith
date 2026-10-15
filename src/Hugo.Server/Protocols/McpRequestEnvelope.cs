namespace Hugo.Server.Protocols
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The parts of a JSON-RPC request the MCP proxy reads: method, id, and params.name.
    /// </summary>
    public class McpRequestEnvelope
    {
        /// <summary>
        /// JSON-RPC method.
        /// </summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; } = null;

        /// <summary>
        /// JSON-RPC id (string or number), or null for a notification.
        /// </summary>
        [JsonPropertyName("id")]
        public JsonElement? Id { get; set; } = null;

        /// <summary>
        /// Params.
        /// </summary>
        [JsonPropertyName("params")]
        public McpRequestParams? Params { get; set; } = null;
    }
}
