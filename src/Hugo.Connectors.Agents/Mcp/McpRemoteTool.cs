namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Text.Json.Serialization;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One tool discovered on an external MCP server, with its annotation hints and the effect Hugo derives from them.
    /// Treat as immutable after discovery.
    /// </summary>
    public class McpRemoteTool
    {
        #region Public-Members

        /// <summary>
        /// Remote tool name, exactly as the server reports it.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Display title, or null.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description written by the remote server (third-party content).
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Input schema as compact JSON, or null when the server sent none.
        /// </summary>
        [JsonIgnore]
        public string? InputSchemaJson { get; set; } = null;

        /// <summary>
        /// <c>annotations.readOnlyHint</c>, or null when absent.
        /// </summary>
        [JsonPropertyName("readOnlyHint")]
        public bool? ReadOnlyHint { get; set; } = null;

        /// <summary>
        /// <c>annotations.destructiveHint</c>, or null when absent.
        /// </summary>
        [JsonPropertyName("destructiveHint")]
        public bool? DestructiveHint { get; set; } = null;

        /// <summary>
        /// <c>annotations.idempotentHint</c>, or null when absent.
        /// </summary>
        [JsonPropertyName("idempotentHint")]
        public bool? IdempotentHint { get; set; } = null;

        /// <summary>
        /// <c>annotations.openWorldHint</c>, or null when absent.
        /// </summary>
        [JsonPropertyName("openWorldHint")]
        public bool? OpenWorldHint { get; set; } = null;

        /// <summary>
        /// Effect derived from the annotations by <see cref="McpEffectClassifier"/>.
        /// </summary>
        [JsonIgnore]
        public EffectEnum Effect
        {
            get
            {
                return McpEffectClassifier.FromHints(ReadOnlyHint, DestructiveHint);
            }
        }

        /// <summary>
        /// Effect as text (<c>Read</c>, <c>Write</c>, <c>Destructive</c>) for structured output.
        /// </summary>
        [JsonPropertyName("effect")]
        public string EffectName
        {
            get
            {
                return Effect.ToString();
            }
        }

        #endregion
    }
}
