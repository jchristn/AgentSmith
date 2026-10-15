namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using SyslogLogging;

    /// <summary>
    /// Builds PolyPrompt completion and embedding clients from a <see cref="ModelEndpointConfig"/> (plan 13.1,
    /// borrowed from Armada <c>ModelEndpointClientFactory</c> and Pneuma <c>ModelClientFactory</c>). Every client shares
    /// one <see cref="HttpClient"/> whose pipeline includes <see cref="RetryAfterCaptureHandler"/>. Clients are cheap
    /// to build; callers create one per call and dispose it (disposing a client does not dispose the shared
    /// HttpClient). Vertex AI service-account credentials are cached per key so access tokens are reused.
    /// Thread-safe.
    /// </summary>
    public class ModelClientFactory : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Logging module handed to PolyPrompt clients. Never null.
        /// </summary>
        public LoggingModule Logging
        {
            get
            {
                return _Logging;
            }
        }

        #endregion

        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly HttpClient _HttpClient;
        private readonly ConcurrentDictionary<string, ServiceAccountCredential> _VertexCredentials = new ConcurrentDictionary<string, ServiceAccountCredential>(StringComparer.Ordinal);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module for PolyPrompt clients. Null creates a quiet module (console off).</param>
        /// <param name="innerHandler">Innermost HTTP handler. Null uses a pooled <see cref="SocketsHttpHandler"/>.
        /// Tests may supply a handler. The factory owns and disposes it.</param>
        public ModelClientFactory(LoggingModule? logging = null, HttpMessageHandler? innerHandler = null)
        {
            if (logging == null)
            {
                logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
            }
            _Logging = logging;
            HttpMessageHandler inner = innerHandler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            _HttpClient = new HttpClient(new RetryAfterCaptureHandler(inner), true);
            _HttpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a provider can serve a kind. Anthropic has no embeddings API and Voyage AI has no inference API.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="kind">Kind.</param>
        /// <returns>True when supported.</returns>
        public static bool SupportsKind(ModelProviderEnum provider, ModelKindEnum kind)
        {
            return UnsupportedReason(provider, kind) == null;
        }

        /// <summary>
        /// Reason a provider cannot serve a kind, or null when it can.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="kind">Kind.</param>
        /// <returns>Reason or null.</returns>
        public static string? UnsupportedReason(ModelProviderEnum provider, ModelKindEnum kind)
        {
            if (kind == ModelKindEnum.Embedding && provider == ModelProviderEnum.Anthropic)
                return "Anthropic does not provide an embeddings API; choose the Inference kind or another provider.";
            if (kind == ModelKindEnum.Inference && provider == ModelProviderEnum.VoyageAI)
                return "Voyage AI provides embeddings only; choose the Embedding kind or another provider.";
            return null;
        }

        /// <summary>
        /// Validate an endpoint configuration for its provider and kind without contacting it.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <returns>Problems found; empty when valid. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
        public static List<string> Validate(ModelEndpointConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            List<string> problems = new List<string>();
            string? reason = UnsupportedReason(config.Provider, config.Kind);
            if (reason != null) problems.Add(reason);
            switch (config.Provider)
            {
                case ModelProviderEnum.OpenAICompatible:
                    if (String.IsNullOrWhiteSpace(config.BaseUrl)) problems.Add("OpenAI-compatible endpoints require a base URL.");
                    if (String.IsNullOrWhiteSpace(config.Model)) problems.Add("OpenAI-compatible endpoints require a model.");
                    break;
                case ModelProviderEnum.AzureOpenAI:
                    if (String.IsNullOrWhiteSpace(config.BaseUrl)) problems.Add("Azure OpenAI requires a base URL (the resource endpoint).");
                    if (String.IsNullOrWhiteSpace(config.DeploymentName) && String.IsNullOrWhiteSpace(config.Model)) problems.Add("Azure OpenAI requires a deployment name.");
                    if (String.IsNullOrWhiteSpace(config.ApiKey)) problems.Add("Azure OpenAI requires an API key.");
                    break;
                case ModelProviderEnum.Bedrock:
                    if (String.IsNullOrWhiteSpace(config.Region)) problems.Add("Bedrock requires a region.");
                    if (String.IsNullOrWhiteSpace(config.AccessKeyId)) problems.Add("Bedrock requires an access key ID.");
                    if (String.IsNullOrWhiteSpace(config.ApiKey)) problems.Add("Bedrock requires a secret access key as the API key.");
                    if (String.IsNullOrWhiteSpace(config.Model)) problems.Add("Bedrock requires a model ID.");
                    break;
                case ModelProviderEnum.VertexAI:
                    if (String.IsNullOrWhiteSpace(config.Project)) problems.Add("Vertex AI requires a project.");
                    if (String.IsNullOrWhiteSpace(config.Region)) problems.Add("Vertex AI requires a region.");
                    if (String.IsNullOrWhiteSpace(config.ApiKey)) problems.Add("Vertex AI requires the service-account JSON as the API key.");
                    if (String.IsNullOrWhiteSpace(config.Model)) problems.Add("Vertex AI requires a model.");
                    break;
                case ModelProviderEnum.OpenAI:
                case ModelProviderEnum.Anthropic:
                case ModelProviderEnum.Gemini:
                case ModelProviderEnum.VoyageAI:
                    if (String.IsNullOrWhiteSpace(config.ApiKey)) problems.Add(config.Provider.ToString() + " requires an API key.");
                    if (String.IsNullOrWhiteSpace(config.Model)) problems.Add(config.Provider.ToString() + " requires a model.");
                    break;
                default:
                    if (String.IsNullOrWhiteSpace(config.Model)) problems.Add("A model is required.");
                    break;
            }
            return problems;
        }

        /// <summary>
        /// Build a completion client. The caller owns and disposes it.
        /// </summary>
        /// <param name="config">Endpoint configuration.</param>
        /// <returns>Client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider cannot serve inference or required fields are missing.</exception>
        public CompletionClientBase CreateCompletion(ModelEndpointConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            ObjectDisposedException.ThrowIf(_Disposed, this);
            string? reason = UnsupportedReason(config.Provider, ModelKindEnum.Inference);
            if (reason != null) throw new InvalidOperationException(reason);

            string? baseUrl = String.IsNullOrWhiteSpace(config.BaseUrl) ? null : config.BaseUrl;
            string? apiKey = String.IsNullOrEmpty(config.ApiKey) ? null : config.ApiKey;
            CompletionClientBase client;
            switch (config.Provider)
            {
                case ModelProviderEnum.Ollama:
                    client = baseUrl == null
                        ? new OllamaCompletionClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new OllamaCompletionClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.OpenAI:
                case ModelProviderEnum.OpenAICompatible:
                    client = baseUrl == null
                        ? new OpenAiCompletionClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new OpenAiCompletionClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.Anthropic:
                    client = baseUrl == null
                        ? new AnthropicCompletionClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new AnthropicCompletionClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.Gemini:
                    client = baseUrl == null
                        ? new GeminiCompletionClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new GeminiCompletionClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.AzureOpenAI:
                    client = new AzureOpenAiCompletionClient(Require(baseUrl, "Azure OpenAI requires a base URL."), AzureDeployment(config), Require(apiKey, "Azure OpenAI requires an API key."), config.ApiVersion, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.VertexAI:
                    client = new VertexAiCompletionClient(Require(config.Project, "Vertex AI requires a project."), Require(config.Region, "Vertex AI requires a region."), VertexCredential(apiKey), baseUrl, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.Bedrock:
                    client = new BedrockCompletionClient(BedrockCredential(config, apiKey), Require(config.Region, "Bedrock requires a region."), baseUrl, _Logging, _HttpClient);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported inference provider " + config.Provider.ToString() + ".");
            }

            if (config.Provider != ModelProviderEnum.AzureOpenAI && !String.IsNullOrWhiteSpace(config.Model)) client.Model = config.Model;
            client.TimeoutMs = config.TimeoutSeconds * 1000;
            return client;
        }

        /// <summary>
        /// Build an embedding client. The caller owns and disposes it.
        /// </summary>
        /// <param name="config">Endpoint configuration.</param>
        /// <returns>Client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider cannot serve embeddings or required fields are missing.</exception>
        public EmbeddingClientBase CreateEmbedding(ModelEndpointConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            ObjectDisposedException.ThrowIf(_Disposed, this);
            string? reason = UnsupportedReason(config.Provider, ModelKindEnum.Embedding);
            if (reason != null) throw new InvalidOperationException(reason);

            string? baseUrl = String.IsNullOrWhiteSpace(config.BaseUrl) ? null : config.BaseUrl;
            string? apiKey = String.IsNullOrEmpty(config.ApiKey) ? null : config.ApiKey;
            EmbeddingClientBase client;
            switch (config.Provider)
            {
                case ModelProviderEnum.Ollama:
                    client = baseUrl == null
                        ? new OllamaEmbeddingClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new OllamaEmbeddingClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.OpenAI:
                case ModelProviderEnum.OpenAICompatible:
                    client = baseUrl == null
                        ? new OpenAiEmbeddingClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new OpenAiEmbeddingClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.Gemini:
                    client = baseUrl == null
                        ? new GeminiEmbeddingClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new GeminiEmbeddingClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.VoyageAI:
                    client = baseUrl == null
                        ? new VoyageAiEmbeddingClient(apiKey: apiKey, logging: _Logging, httpClient: _HttpClient)
                        : new VoyageAiEmbeddingClient(baseUrl, apiKey, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.AzureOpenAI:
                    client = new AzureOpenAiEmbeddingClient(Require(baseUrl, "Azure OpenAI requires a base URL."), AzureDeployment(config), Require(apiKey, "Azure OpenAI requires an API key."), config.ApiVersion, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.VertexAI:
                    client = new VertexAiEmbeddingClient(Require(config.Project, "Vertex AI requires a project."), Require(config.Region, "Vertex AI requires a region."), VertexCredential(apiKey), baseUrl, _Logging, _HttpClient);
                    break;
                case ModelProviderEnum.Bedrock:
                    client = new BedrockEmbeddingClient(BedrockCredential(config, apiKey), Require(config.Region, "Bedrock requires a region."), baseUrl, _Logging, _HttpClient);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported embedding provider " + config.Provider.ToString() + ".");
            }

            if (config.Provider != ModelProviderEnum.AzureOpenAI && !String.IsNullOrWhiteSpace(config.Model)) client.Model = config.Model;
            client.TimeoutMs = config.TimeoutSeconds * 1000;
            return client;
        }

        /// <summary>
        /// Dispose the shared HTTP client and cached credentials.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (disposing)
            {
                foreach (ServiceAccountCredential credential in _VertexCredentials.Values) credential.Dispose();
                _VertexCredentials.Clear();
                _HttpClient.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        private static string Require(string? value, string message)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
            return value;
        }

        private static string AzureDeployment(ModelEndpointConfig config)
        {
            if (!String.IsNullOrWhiteSpace(config.DeploymentName)) return config.DeploymentName;
            return Require(config.Model, "Azure OpenAI requires a deployment name.");
        }

        private ICredentialProvider VertexCredential(string? apiKey)
        {
            string json = Require(apiKey, "Vertex AI requires the service-account JSON as the API key.");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            return _VertexCredentials.GetOrAdd(hash, _ => ServiceAccountCredential.FromJson(json, null, _HttpClient));
        }

        private static IAwsCredentialProvider BedrockCredential(ModelEndpointConfig config, string? apiKey)
        {
            return new StaticAwsCredential(
                Require(config.AccessKeyId, "Bedrock requires an access key ID."),
                Require(apiKey, "Bedrock requires a secret access key as the API key."),
                Require(config.Region, "Bedrock requires a region."),
                null);
        }

        #endregion
    }
}
