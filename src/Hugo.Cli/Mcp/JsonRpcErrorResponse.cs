namespace Hugo.Cli.Mcp
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON-RPC error response the relay writes when it cannot deliver a request (server unreachable, credentials
    /// rejected, response stream broken).
    /// </summary>
    public class JsonRpcErrorResponse
    {
        /// <summary>
        /// Always <c>2.0</c>.
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>
        /// ID of the request being answered. Written as JSON null when unknown.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public JsonElement? Id { get; set; } = null;

        /// <summary>
        /// The error. Never null.
        /// </summary>
        [JsonPropertyName("error")]
        public JsonRpcErrorBody Error { get; set; } = new JsonRpcErrorBody();
    }
}
