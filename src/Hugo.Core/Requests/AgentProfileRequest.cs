namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Create or update an agent profile. Null fields keep their value; an empty string clears an optional text or endpoint
    /// field.
    /// </summary>
    public class AgentProfileRequest
    {
        #region Public-Members

        /// <summary>
        /// URL-safe name, unique per tenant. Required on create.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Persona self-name. Default Agent Smith.
        /// </summary>
        public string? PersonaName { get; set; } = null;

        /// <summary>
        /// How the persona addresses the operator. Default Mr. Anderson.
        /// </summary>
        public string? UserAddress { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Persona template key. Default persona.agent-smith.
        /// </summary>
        public string? PersonaTemplateKey { get; set; } = null;

        /// <summary>
        /// Inference endpoint.
        /// </summary>
        public string? InferenceEndpointId { get; set; } = null;

        /// <summary>
        /// Fallback inference endpoint.
        /// </summary>
        public string? FallbackInferenceEndpointId { get; set; } = null;

        /// <summary>
        /// Embedding endpoint.
        /// </summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>
        /// Maximum iterations per turn (1 to 100).
        /// </summary>
        public int? MaxIterations { get; set; } = null;

        /// <summary>
        /// Maximum tool calls per turn (1 to 500).
        /// </summary>
        public int? MaxToolCallsPerTurn { get; set; } = null;

        /// <summary>
        /// Tool timeout in seconds (1 to 3600).
        /// </summary>
        public int? ToolTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Sampling temperature (0 to 2).
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// A2A exposure.
        /// </summary>
        public bool? ExposeA2A { get; set; } = null;

        /// <summary>
        /// MCP exposure.
        /// </summary>
        public bool? ExposeMcp { get; set; } = null;

        /// <summary>
        /// OpenAI-compatible exposure.
        /// </summary>
        public bool? ExposeOpenAI { get; set; } = null;

        /// <summary>
        /// Management tools exposure.
        /// </summary>
        public bool? ExposeManagementTools { get; set; } = null;

        /// <summary>
        /// Connector guidance detail: Auto, Full, or Compact. Null leaves it unchanged.
        /// </summary>
        public InstructionDetailEnum? InstructionDetail { get; set; } = null;

        /// <summary>
        /// Profile append text.
        /// </summary>
        public string? InstructionsAppend { get; set; } = null;

        /// <summary>
        /// Make this the tenant default profile (true only).
        /// </summary>
        public bool? IsDefault { get; set; } = null;

        /// <summary>
        /// Private or Tenant.
        /// </summary>
        public VisibilityEnum? Visibility { get; set; } = null;

        /// <summary>
        /// Whether the profile is active.
        /// </summary>
        public bool? Active { get; set; } = null;

        #endregion
    }
}
