namespace Hugo.Server.Protocols
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The parts of a JSON-RPC request's params the MCP proxy reads: the tool name of tools/call.
    /// </summary>
    public class McpRequestParams
    {
        /// <summary>
        /// Tool, prompt, or resource name (params.name).
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;
    }
}
