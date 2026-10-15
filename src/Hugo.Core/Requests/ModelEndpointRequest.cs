namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Core.Inference;

    /// <summary>
    /// Create or update a model endpoint. Null fields keep their value; an empty string clears an optional text field.
    /// </summary>
    public class ModelEndpointRequest
    {
        #region Public-Members

        /// <summary>
        /// Name, unique per tenant. Required on create.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Inference or Embedding. Default Inference.
        /// </summary>
        public ModelKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Provider. Required on create.
        /// </summary>
        public ModelProviderEnum? Provider { get; set; } = null;

        /// <summary>
        /// Base URL.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Model name. Required on create.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Vault secret with the API key.
        /// </summary>
        public string? ApiKeySecretId { get; set; } = null;

        /// <summary>
        /// Field of that secret to use.
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
        public int? ContextWindowTokens { get; set; } = null;

        /// <summary>
        /// Maximum output tokens.
        /// </summary>
        public int? MaxOutputTokens { get; set; } = null;

        /// <summary>
        /// Tool support.
        /// </summary>
        public bool? SupportsTools { get; set; } = null;

        /// <summary>
        /// Streaming support.
        /// </summary>
        public bool? SupportsStreaming { get; set; } = null;

        /// <summary>
        /// Embedding dimensions.
        /// </summary>
        public int? EmbeddingDimensions { get; set; } = null;

        /// <summary>
        /// Concurrent requests.
        /// </summary>
        public int? MaxConcurrency { get; set; } = null;

        /// <summary>
        /// Timeout in seconds.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Retries.
        /// </summary>
        public int? MaxRetries { get; set; } = null;

        /// <summary>
        /// Whether the endpoint is active.
        /// </summary>
        public bool? Active { get; set; } = null;

        #endregion
    }
}
