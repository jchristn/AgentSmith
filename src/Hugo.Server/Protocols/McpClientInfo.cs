namespace Hugo.Server.Protocols
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// clientInfo of an MCP initialize request.
    /// </summary>
    public class McpClientInfo
    {
        /// <summary>
        /// Client name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Client version.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; } = null;
    }
}
