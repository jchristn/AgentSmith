namespace Hugo.Cli.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The part of an <c>initialize</c> result the relay needs: the negotiated protocol version, sent afterwards in
    /// the <c>MCP-Protocol-Version</c> header.
    /// </summary>
    public class McpInitializeResult
    {
        /// <summary>
        /// Negotiated protocol version, or null.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? ProtocolVersion { get; set; } = null;
    }
}
