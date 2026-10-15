namespace Hugo.Server.Protocols
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON-RPC error response written by the MCP proxy (authentication, profile resolution, transport failures).
    /// </summary>
    public class McpErrorEnvelope
    {
        /// <summary>
        /// JSON-RPC version.
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>
        /// Id of the request, or null.
        /// </summary>
        [JsonPropertyName("id")]
        public JsonElement? Id { get; set; } = null;

        /// <summary>
        /// Error.
        /// </summary>
        [JsonPropertyName("error")]
        public McpErrorDetail Error { get; set; } = new McpErrorDetail();
    }
}
