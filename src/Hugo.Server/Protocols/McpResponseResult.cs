namespace Hugo.Server.Protocols
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The part of a JSON-RPC result the MCP proxy reads for telemetry: whether a tool result is an error.
    /// </summary>
    public class McpResponseResult
    {
        /// <summary>
        /// isError of a tools/call result.
        /// </summary>
        [JsonPropertyName("isError")]
        public bool? IsError { get; set; } = null;
    }
}
