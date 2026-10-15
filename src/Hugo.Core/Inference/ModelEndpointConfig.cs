namespace Hugo.Core.Inference
{
    using System;

    /// <summary>
    /// Runtime configuration of one model endpoint, as the gateway needs it for a single call. The server builds
    /// it from the persisted <c>model_endpoints</c> row and supplies the decrypted API key at call time. The API
    /// key is plaintext in memory only for the call; it is never logged and <see cref="ToString"/> omits it.
    /// Not thread-safe for mutation; treat as immutable once handed to the gateway.
    /// </summary>
    public class ModelEndpointConfig
    {
        #region Public-Members

        /// <summary>
        /// Endpoint identifier. Keys the concurrency gate and the circuit breaker. When empty, a key is derived
        /// from the provider, base URL, and model (see <see cref="Key"/>). Default empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Human name for logs and events. Default empty.
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
        /// Provider. Default <see cref="ModelProviderEnum.OpenAICompatible"/>.
        /// </summary>
        public ModelProviderEnum Provider { get; set; } = ModelProviderEnum.OpenAICompatible;

        /// <summary>
        /// What the endpoint serves. Default <see cref="ModelKindEnum.Inference"/>.
        /// </summary>
        public ModelKindEnum Kind { get; set; } = ModelKindEnum.Inference;

        /// <summary>
        /// Base URL (for example <c>http://127.0.0.1:11434</c> or <c>https://api.openai.com</c>). Optional for
        /// providers with a well-known default (OpenAI, Anthropic, Gemini, Voyage AI); an override for Bedrock and
        /// Vertex AI; required for Azure OpenAI and OpenAI-compatible servers. Null means the provider default.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Model name (for example <c>gpt-4o-mini</c>, <c>llama3.1:8b</c>). For Azure OpenAI this is ignored in favor
        /// of <see cref="DeploymentName"/> when that is set. Default empty.
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
        /// Plaintext API key (or secret access key for Bedrock, or service-account JSON for Vertex AI), resolved
        /// from the secret vault for this call only. Never logged, never serialized by Hugo. Null when the
        /// provider needs no key.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// Cloud region (Bedrock, Vertex AI). Null when not applicable.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Azure OpenAI deployment name. When null, <see cref="Model"/> is used as the deployment name.
        /// </summary>
        public string? DeploymentName { get; set; } = null;

        /// <summary>
        /// Azure OpenAI API version. Null uses the PolyPrompt default.
        /// </summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>
        /// Google Cloud project ID (Vertex AI). Null when not applicable.
        /// </summary>
        public string? Project { get; set; } = null;

        /// <summary>
        /// AWS access key ID (Bedrock). The secret access key is <see cref="ApiKey"/>. Null when not applicable.
        /// </summary>
        public string? AccessKeyId { get; set; } = null;

        /// <summary>
        /// Context window in tokens. Drives compaction. Default 8192. Minimum 256, maximum 10,000,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int ContextWindowTokens
        {
            get
            {
                return _ContextWindowTokens;
            }
            set
            {
                if (value < 256 || value > 10_000_000) throw new ArgumentOutOfRangeException(nameof(ContextWindowTokens), "ContextWindowTokens must be between 256 and 10,000,000.");
                _ContextWindowTokens = value;
            }
        }

        /// <summary>
        /// Maximum output tokens per completion. Default 2048. Minimum 1, maximum 1,000,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int MaxOutputTokens
        {
            get
            {
                return _MaxOutputTokens;
            }
            set
            {
                if (value < 1 || value > 1_000_000) throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens), "MaxOutputTokens must be between 1 and 1,000,000.");
                _MaxOutputTokens = value;
            }
        }

        /// <summary>
        /// Whether the model supports native tool calling. When false, the runtime sends no tool definitions.
        /// Default true.
        /// </summary>
        public bool SupportsTools { get; set; } = true;

        /// <summary>
        /// Whether the endpoint supports streaming. When false, the runtime uses non-streaming tool chat and
        /// emits the answer as one delta. Default true.
        /// </summary>
        public bool SupportsStreaming { get; set; } = true;

        /// <summary>
        /// Embedding vector dimensions, when known (recorded by validation). Null when unknown.
        /// </summary>
        public int? EmbeddingDimensions { get; set; } = null;

        /// <summary>
        /// Maximum concurrent requests to this endpoint across the process. 0 means unlimited. Default 4.
        /// Minimum 0, maximum 1024.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int MaxConcurrency
        {
            get
            {
                return _MaxConcurrency;
            }
            set
            {
                if (value < 0 || value > 1024) throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), "MaxConcurrency must be between 0 and 1024.");
                _MaxConcurrency = value;
            }
        }

        /// <summary>
        /// Per-request timeout in seconds, covering the whole response including a stream. Default 120.
        /// Minimum 1, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int TimeoutSeconds
        {
            get
            {
                return _TimeoutSeconds;
            }
            set
            {
                if (value < 1 || value > 3600) throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "TimeoutSeconds must be between 1 and 3600.");
                _TimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Retries after the first attempt for retryable failures (429, 5xx, transport errors, timeouts).
        /// Default 2. Minimum 0, maximum 10.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside the range.</exception>
        public int MaxRetries
        {
            get
            {
                return _MaxRetries;
            }
            set
            {
                if (value < 0 || value > 10) throw new ArgumentOutOfRangeException(nameof(MaxRetries), "MaxRetries must be between 0 and 10.");
                _MaxRetries = value;
            }
        }

        /// <summary>
        /// Sampling temperature. Null uses the provider default. Clamped to 0.0 through 2.0.
        /// </summary>
        public double? Temperature
        {
            get
            {
                return _Temperature;
            }
            set
            {
                _Temperature = value.HasValue ? Math.Clamp(value.Value, 0.0, 2.0) : null;
            }
        }

        /// <summary>
        /// Gate and circuit-breaker key: <see cref="Id"/> when set, otherwise provider, base URL, and model.
        /// </summary>
        public string Key
        {
            get
            {
                if (!String.IsNullOrEmpty(_Id)) return _Id;
                return Provider.ToString() + "|" + (BaseUrl ?? String.Empty) + "|" + _Model;
            }
        }

        #endregion

        #region Private-Members

        private string _Id = String.Empty;
        private string _Name = String.Empty;
        private string _Model = String.Empty;
        private int _ContextWindowTokens = 8192;
        private int _MaxOutputTokens = 2048;
        private int _MaxConcurrency = 4;
        private int _TimeoutSeconds = 120;
        private int _MaxRetries = 2;
        private double? _Temperature = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Describe the endpoint without the API key.
        /// </summary>
        /// <returns>Description.</returns>
        public override string ToString()
        {
            return Provider.ToString() + " " + Kind.ToString() + " " + (String.IsNullOrEmpty(_Name) ? Key : _Name) + " model=" + _Model;
        }

        #endregion
    }
}
