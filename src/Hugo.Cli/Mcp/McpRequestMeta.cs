namespace Hugo.Cli.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>_meta</c> member of request parameters. Under the stateless revision (2026-07-28) every request names
    /// its protocol version here.
    /// </summary>
    public class McpRequestMeta
    {
        /// <summary>
        /// <c>io.modelcontextprotocol/protocolVersion</c>, or null.
        /// </summary>
        [JsonPropertyName("io.modelcontextprotocol/protocolVersion")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? ProtocolVersion { get; set; } = null;
    }
}
