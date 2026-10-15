namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// An inference or embedding model endpoint (preview).
    /// </summary>
    public class ModelEndpoint
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
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Inference or Embedding.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, AzureOpenAI, Bedrock, VertexAI, or VoyageAI.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Base URL.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// API key secret Identifier.
        /// </summary>
        public string? ApiKeySecretId { get; set; } = null;

        /// <summary>
        /// API key secret field.
        /// </summary>
        public string? ApiKeySecretField { get; set; } = null;

        /// <summary>
        /// Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Deployment name.
        /// </summary>
        public string? DeploymentName { get; set; } = null;

        /// <summary>
        /// API version.
        /// </summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>
        /// Context window tokens.
        /// </summary>
        public int ContextWindowTokens { get; set; } = 0;

        /// <summary>
        /// Max output tokens.
        /// </summary>
        public int MaxOutputTokens { get; set; } = 0;

        /// <summary>
        /// Supports tools.
        /// </summary>
        public bool SupportsTools { get; set; } = false;

        /// <summary>
        /// Supports streaming.
        /// </summary>
        public bool SupportsStreaming { get; set; } = false;

        /// <summary>
        /// Embedding dimensions.
        /// </summary>
        public int? EmbeddingDimensions { get; set; } = null;

        /// <summary>
        /// Max concurrency.
        /// </summary>
        public int MaxConcurrency { get; set; } = 0;

        /// <summary>
        /// Timeout seconds.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 0;

        /// <summary>
        /// Max retries.
        /// </summary>
        public int MaxRetries { get; set; } = 0;

        /// <summary>
        /// Unknown, Healthy, or Unhealthy.
        /// </summary>
        public string? HealthStatus { get; set; } = null;

        /// <summary>
        /// Last health UTC.
        /// </summary>
        public DateTime? LastHealthUtc { get; set; } = null;

        /// <summary>
        /// Last health message.
        /// </summary>
        public string? LastHealthMessage { get; set; } = null;

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
