namespace Hugo.Connectors.Agents.Mcp.Wire
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Wire shape of the MCP <c>initialize</c> result. Unknown members are ignored. Not thread-safe.
    /// </summary>
    public class McpWireInitializeResult
    {
        /// <summary>
        /// Negotiated protocol version, or null.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Server capabilities, or null.
        /// </summary>
        [JsonPropertyName("capabilities")]
        public McpWireServerCapabilities? Capabilities { get; set; } = null;

        /// <summary>
        /// Server name and version, or null.
        /// </summary>
        [JsonPropertyName("serverInfo")]
        public McpWireImplementation? ServerInfo { get; set; } = null;

        /// <summary>
        /// The server's instructions for clients (third-party content), or null.
        /// </summary>
        [JsonPropertyName("instructions")]
        public string? Instructions { get; set; } = null;
    }
}
