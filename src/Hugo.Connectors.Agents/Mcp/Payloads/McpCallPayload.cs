namespace Hugo.Connectors.Agents.Mcp.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>mcp_call_tool</c>. Content comes from the remote server. Not thread-safe.
    /// </summary>
    public class McpCallPayload
    {
        /// <summary>
        /// Remote tool name.
        /// </summary>
        [JsonPropertyName("tool")]
        public string Tool { get; set; } = String.Empty;

        /// <summary>
        /// Server name and version.
        /// </summary>
        [JsonPropertyName("server")]
        public string Server { get; set; } = String.Empty;

        /// <summary>
        /// Effect the call was classified with.
        /// </summary>
        [JsonPropertyName("effect")]
        public string Effect { get; set; } = String.Empty;

        /// <summary>
        /// Whether the remote tool reported an error.
        /// </summary>
        [JsonPropertyName("isError")]
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Content blocks (binary data summarized).
        /// </summary>
        [JsonPropertyName("content")]
        public List<McpContentPayload> Content { get; set; } = new List<McpContentPayload>();

        /// <summary>
        /// The remote structuredContent, passed through, or null.
        /// </summary>
        [JsonPropertyName("structuredContent")]
        public JsonElement? StructuredContent { get; set; } = null;
    }
}
