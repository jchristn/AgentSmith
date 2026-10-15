namespace Hugo.Server.Protocols
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Params of an MCP initialize request.
    /// </summary>
    public class McpInitializeParams
    {
        /// <summary>
        /// Requested protocol revision.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Client capabilities (an object; only its presence is checked).
        /// </summary>
        [JsonPropertyName("capabilities")]
        public JsonElement? Capabilities { get; set; } = null;

        /// <summary>
        /// Client identity.
        /// </summary>
        [JsonPropertyName("clientInfo")]
        public McpClientInfo? ClientInfo { get; set; } = null;
    }
}
