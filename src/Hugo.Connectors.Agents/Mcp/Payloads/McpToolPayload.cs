namespace Hugo.Connectors.Agents.Mcp.Payloads
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One remote tool in <c>mcp_list_tools</c> output. Not thread-safe.
    /// </summary>
    public class McpToolPayload
    {
        /// <summary>
        /// Remote tool name; pass it as <c>name</c> to mcp_call_tool.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Display title, or null.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description from the remote server (clipped).
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Effect Hugo derives from the annotations: Read, Write, or Destructive.
        /// </summary>
        [JsonPropertyName("effect")]
        public string Effect { get; set; } = String.Empty;

        /// <summary>
        /// readOnlyHint, or null when absent.
        /// </summary>
        [JsonPropertyName("readOnlyHint")]
        public bool? ReadOnlyHint { get; set; } = null;

        /// <summary>
        /// destructiveHint, or null when absent.
        /// </summary>
        [JsonPropertyName("destructiveHint")]
        public bool? DestructiveHint { get; set; } = null;

        /// <summary>
        /// idempotentHint, or null when absent.
        /// </summary>
        [JsonPropertyName("idempotentHint")]
        public bool? IdempotentHint { get; set; } = null;

        /// <summary>
        /// openWorldHint, or null when absent.
        /// </summary>
        [JsonPropertyName("openWorldHint")]
        public bool? OpenWorldHint { get; set; } = null;

        /// <summary>
        /// Input schema, or null when omitted or larger than the schema limit.
        /// </summary>
        [JsonPropertyName("inputSchema")]
        public JsonElement? InputSchema { get; set; } = null;

        /// <summary>
        /// Whether a schema existed but was omitted (not requested or too large).
        /// </summary>
        [JsonPropertyName("schemaOmitted")]
        public bool SchemaOmitted { get; set; } = false;
    }
}
