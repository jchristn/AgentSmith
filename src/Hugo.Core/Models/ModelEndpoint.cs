namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;
    using Hugo.Core.Inference;

    /// <summary>
    /// A tenant's inference or embedding endpoint (model_endpoints, plan sections 7.4 and 13.1). The API key is a vault
    /// secret reference. Not thread-safe.
    /// </summary>
    public class ModelEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix mep_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewModelEndpointId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Name, unique per tenant.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Inference or Embedding.
        /// </summary>
        public ModelKindEnum Kind { get; set; } = ModelKindEnum.Inference;

        /// <summary>
        /// Provider.
        /// </summary>
        public ModelProviderEnum Provider { get; set; } = ModelProviderEnum.OpenAICompatible;

        /// <summary>
        /// Base URL, or null for the provider default.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Model name.
        /// </summary>
        public string Model
        {
            get
            {
                return _Model;
            }
            set
            {
                _Model = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Vault secret holding the API key, or null.
        /// </summary>
        public string? ApiKeySecretId { get; set; } = null;

        /// <summary>
        /// Field of the secret to use, or null for the secret kind's primary field.
        /// </summary>
        public string? ApiKeySecretField { get; set; } = null;

        /// <summary>
        /// Region (Bedrock, Vertex).
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Deployment name (Azure OpenAI).
        /// </summary>
        public string? DeploymentName { get; set; } = null;

        /// <summary>
        /// API version (Azure OpenAI).
        /// </summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>
        /// Context window in tokens. Default 32768, minimum 1024, maximum 10000000.
        /// </summary>
        public int ContextWindowTokens
        {
            get
            {
                return _ContextWindowTokens;
            }
            set
            {
                _ContextWindowTokens = Math.Clamp(value, 1024, 10000000);
            }
        }

        /// <summary>
        /// Maximum output tokens. Default 4096, minimum 16, maximum 1000000.
        /// </summary>
        public int MaxOutputTokens
        {
            get
            {
                return _MaxOutputTokens;
            }
            set
            {
                _MaxOutputTokens = Math.Clamp(value, 16, 1000000);
            }
        }

        /// <summary>
        /// Whether the model supports tool calls. Default true.
        /// </summary>
        public bool SupportsTools { get; set; } = true;

        /// <summary>
        /// Whether the model streams. Default true.
        /// </summary>
        public bool SupportsStreaming { get; set; } = true;

        /// <summary>
        /// Embedding dimensions, when known.
        /// </summary>
        public int? EmbeddingDimensions { get; set; } = null;

        /// <summary>
        /// Concurrent requests allowed. Default 4, minimum 1, maximum 256.
        /// </summary>
        public int MaxConcurrency
        {
            get
            {
                return _MaxConcurrency;
            }
            set
            {
                _MaxConcurrency = Math.Clamp(value, 1, 256);
            }
        }

        /// <summary>
        /// Request timeout in seconds. Default 120, minimum 1, maximum 3600.
        /// </summary>
        public int TimeoutSeconds
        {
            get
            {
                return _TimeoutSeconds;
            }
            set
            {
                _TimeoutSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Retries on 429 and 5xx. Default 2, minimum 0, maximum 10.
        /// </summary>
        public int MaxRetries
        {
            get
            {
                return _MaxRetries;
            }
            set
            {
                _MaxRetries = Math.Clamp(value, 0, 10);
            }
        }

        /// <summary>
        /// Health from the last check.
        /// </summary>
        public ModelHealthStatusEnum HealthStatus { get; set; } = ModelHealthStatusEnum.Unknown;

        /// <summary>
        /// When health was last checked (UTC).
        /// </summary>
        public DateTime? LastHealthUtc { get; set; } = null;

        /// <summary>
        /// Message from the last health check (scrubbed).
        /// </summary>
        public string? LastHealthMessage { get; set; } = null;

        /// <summary>
        /// Whether the endpoint is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewModelEndpointId();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;
        private string _Model = String.Empty;
        private int _ContextWindowTokens = 32768;
        private int _MaxOutputTokens = 4096;
        private int _MaxConcurrency = 4;
        private int _TimeoutSeconds = 120;
        private int _MaxRetries = 2;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
