namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// An agent profile: persona, model endpoints, limits, exposure, and bound resources (preview).
    /// </summary>
    public class AgentProfile
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner user Identifier.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Private or Tenant.
        /// </summary>
        public string? Visibility { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

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
        public int MaxIterations { get; set; } = 0;

        /// <summary>
        /// Max tool calls per turn.
        /// </summary>
        public int MaxToolCallsPerTurn { get; set; } = 0;

        /// <summary>
        /// Tool timeout seconds.
        /// </summary>
        public int ToolTimeoutSeconds { get; set; } = 0;

        /// <summary>
        /// Temperature.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Whether the profile is reachable over A2A.
        /// </summary>
        public bool ExposeA2A { get; set; } = false;

        /// <summary>
        /// Whether the profile is reachable over MCP.
        /// </summary>
        public bool ExposeMcp { get; set; } = false;

        /// <summary>
        /// Whether the profile is reachable over the OpenAI-compatible API.
        /// </summary>
        public bool ExposeOpenAI { get; set; } = false;

        /// <summary>
        /// Expose management tools.
        /// </summary>
        public bool ExposeManagementTools { get; set; } = false;

        /// <summary>
        /// Instructions append.
        /// </summary>
        public string? InstructionsAppend { get; set; } = null;

        /// <summary>
        /// Is default.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = false;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
