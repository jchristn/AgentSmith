namespace Hugo.Sdk.Requests
{
    using System.Text.Json.Serialization;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Create or update an agent profile. Null properties are omitted.
    /// </summary>
    public class AgentProfileRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Persona name.
        /// </summary>
        public string? PersonaName { get; set; } = null;

        /// <summary>
        /// User address.
        /// </summary>
        public string? UserAddress { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Persona template key.
        /// </summary>
        public string? PersonaTemplateKey { get; set; } = null;

        /// <summary>
        /// Inference endpoint Identifier.
        /// </summary>
        public string? InferenceEndpointId { get; set; } = null;

        /// <summary>
        /// Fallback inference endpoint Identifier.
        /// </summary>
        public string? FallbackInferenceEndpointId { get; set; } = null;

        /// <summary>
        /// Embedding endpoint Identifier.
        /// </summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>
        /// Max iterations.
        /// </summary>
        public int? MaxIterations { get; set; } = null;

        /// <summary>
        /// Max tool calls per turn.
        /// </summary>
        public int? MaxToolCallsPerTurn { get; set; } = null;

        /// <summary>
        /// Tool timeout seconds.
        /// </summary>
        public int? ToolTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Temperature.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Expose A2A.
        /// </summary>
        [JsonPropertyName("exposeA2A")]
        public bool? ExposeA2A { get; set; } = null;

        /// <summary>
        /// Expose MCP.
        /// </summary>
        public bool? ExposeMcp { get; set; } = null;

        /// <summary>
        /// Expose OpenAI.
        /// </summary>
        [JsonPropertyName("exposeOpenAI")]
        public bool? ExposeOpenAI { get; set; } = null;

        /// <summary>
        /// Expose management tools.
        /// </summary>
        public bool? ExposeManagementTools { get; set; } = null;

        /// <summary>
        /// Instructions append.
        /// </summary>
        public string? InstructionsAppend { get; set; } = null;

        /// <summary>
        /// Is default.
        /// </summary>
        public bool? IsDefault { get; set; } = null;

        /// <summary>
        /// Private or Tenant.
        /// </summary>
        public string? Visibility { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        #endregion
    }
}
