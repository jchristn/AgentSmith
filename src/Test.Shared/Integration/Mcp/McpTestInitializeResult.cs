namespace Test.Shared.Integration.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// An initialize result as the tests read it.
    /// </summary>
    public class McpTestInitializeResult
    {
        /// <summary>
        /// Negotiated revision.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Instructions.
        /// </summary>
        [JsonPropertyName("instructions")]
        public string? Instructions { get; set; } = null;

        /// <summary>
        /// Server identity.
        /// </summary>
        [JsonPropertyName("serverInfo")]
        public McpTestContent? ServerInfo { get; set; } = null;
    }
}
