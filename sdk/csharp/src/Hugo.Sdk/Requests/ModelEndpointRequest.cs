namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Create or update a model endpoint. Null properties are omitted.
    /// </summary>
    public class ModelEndpointRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Inference or Embedding.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Provider.
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
        public int? ContextWindowTokens { get; set; } = null;

        /// <summary>
        /// Max output tokens.
        /// </summary>
        public int? MaxOutputTokens { get; set; } = null;

        /// <summary>
        /// Supports tools.
        /// </summary>
        public bool? SupportsTools { get; set; } = null;

        /// <summary>
        /// Supports streaming.
        /// </summary>
        public bool? SupportsStreaming { get; set; } = null;

        /// <summary>
        /// Embedding dimensions.
        /// </summary>
        public int? EmbeddingDimensions { get; set; } = null;

        /// <summary>
        /// Max concurrency.
        /// </summary>
        public int? MaxConcurrency { get; set; } = null;

        /// <summary>
        /// Timeout seconds.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Max retries.
        /// </summary>
        public int? MaxRetries { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        #endregion
    }
}
