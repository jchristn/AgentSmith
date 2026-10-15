namespace Hugo.Cli.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The part of a <c>tools/list</c> result the relay reads to learn which tool parameters are mirrored into
    /// <c>Mcp-Param-{Name}</c> headers (2026-07-28).
    /// </summary>
    public class McpToolsListResult
    {
        /// <summary>
        /// Tools, or null.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<McpToolSummary>? Tools { get; set; } = null;
    }
}
