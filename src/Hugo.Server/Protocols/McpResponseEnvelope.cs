namespace Hugo.Server.Protocols
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The parts of a JSON-RPC response the MCP proxy reads for telemetry.
    /// </summary>
    public class McpResponseEnvelope
    {
        /// <summary>
        /// Error, when the request failed.
        /// </summary>
        [JsonPropertyName("error")]
        public McpErrorDetail? Error { get; set; } = null;

        /// <summary>
        /// Result, when the request succeeded.
        /// </summary>
        [JsonPropertyName("result")]
        public McpResponseResult? Result { get; set; } = null;
    }
}
