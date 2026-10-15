namespace Hugo.Connectors.Agents.Mcp.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>mcp_list_resources</c>. Not thread-safe.
    /// </summary>
    public class McpResourceListPayload
    {
        /// <summary>
        /// Server name and version.
        /// </summary>
        [JsonPropertyName("server")]
        public string Server { get; set; } = String.Empty;

        /// <summary>
        /// Resources the server listed.
        /// </summary>
        [JsonPropertyName("discovered")]
        public int Discovered { get; set; } = 0;

        /// <summary>
        /// Resources in this result (bounded by the row limit).
        /// </summary>
        [JsonPropertyName("shown")]
        public int Shown { get; set; } = 0;

        /// <summary>
        /// Whether listing stopped at the page cap.
        /// </summary>
        [JsonPropertyName("incomplete")]
        public bool Incomplete { get; set; } = false;

        /// <summary>
        /// Resources.
        /// </summary>
        [JsonPropertyName("resources")]
        public List<McpResourcePayload> Resources { get; set; } = new List<McpResourcePayload>();
    }
}
