namespace Hugo.Connectors.Agents.Mcp.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>mcp_list_tools</c>. Text members other than counts come from the remote server. Not thread-safe.
    /// </summary>
    public class McpToolListPayload
    {
        /// <summary>
        /// Server name and version as reported in initialize.
        /// </summary>
        [JsonPropertyName("server")]
        public string Server { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint URL or Stdio command.
        /// </summary>
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = String.Empty;

        /// <summary>
        /// Negotiated protocol version, or null.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// The server's instructions (untrusted third-party content, clipped), or null.
        /// </summary>
        [JsonPropertyName("instructions")]
        public string? Instructions { get; set; } = null;

        /// <summary>
        /// Tools the server listed.
        /// </summary>
        [JsonPropertyName("discovered")]
        public int Discovered { get; set; } = 0;

        /// <summary>
        /// Tools allowed by includeTools and excludeTools.
        /// </summary>
        [JsonPropertyName("allowed")]
        public int Allowed { get; set; } = 0;

        /// <summary>
        /// Allowed tools matching the filter argument.
        /// </summary>
        [JsonPropertyName("matched")]
        public int Matched { get; set; } = 0;

        /// <summary>
        /// Tools in this result (bounded by the row limit).
        /// </summary>
        [JsonPropertyName("shown")]
        public int Shown { get; set; } = 0;

        /// <summary>
        /// Whether discovery stopped at the page cap.
        /// </summary>
        [JsonPropertyName("incomplete")]
        public bool Incomplete { get; set; } = false;

        /// <summary>
        /// When the list was fetched (ISO 8601, UTC).
        /// </summary>
        [JsonPropertyName("fetchedUtc")]
        public string FetchedUtc { get; set; } = String.Empty;

        /// <summary>
        /// Tools.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<McpToolPayload> Tools { get; set; } = new List<McpToolPayload>();
    }
}
