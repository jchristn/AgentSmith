namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Secrets;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Model endpoints (P6.2, P6.5, P6.6): CRUD with vault secret references for API keys, conversion to the runtime
    /// <see cref="ModelEndpointConfig"/> (the key is decrypted only for the call), validation through the model
    /// gateway, provider model listing where PolyPrompt supports it, and the health sweep. Thread-safe.
    /// </summary>
    public class ModelEndpointService
    {
        #region Public-Members

        /// <summary>
        /// Model gateway (concurrency gates, retries, circuit breakers, fallback).
        /// </summary>
        public IModelGateway Gateway
        {
            get
            {
                return _Gateway;
            }
        }

        /// <summary>
        /// Raised with the tenant identifier after any endpoint change.
        /// </summary>
        public event EventHandler<string>? Changed;

        #endregion

        #region Private-Members

        private static readonly Regex _NamePattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        private readonly SecretService _Secrets;
        private readonly IModelGateway _Gateway;
        private readonly AuditService _Audit;
        private readonly LoggingModule _Logging;
        private readonly EventHub? _Events;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="secrets">Secret service.</param>
        /// <param name="gateway">Model gateway.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="logging">Logging.</param>
        /// <param name="events">Event hub, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ModelEndpointService(DatabaseDriverBase database, SecretService secrets, IModelGateway gateway, AuditService audit, LoggingModule logging, EventHub? events)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Events = events;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an endpoint.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint.</returns>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="ConflictException">Thrown when the name is taken.</exception>
        public async Task<ModelEndpoint> CreateAsync(RequestContext ctx, string tenantId, ModelEndpointRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            if (String.IsNullOrWhiteSpace(request.Name) || !_NamePattern.IsMatch(request.Name)) throw Invalid("name", "A name of 1 to 128 letters, digits, dots, hyphens, or underscores is required.");
            if (!request.Provider.HasValue) throw Invalid("provider", "A provider is required.");
            if (await _Database.ModelEndpoints.ReadByNameAsync(tenantId, request.Name, token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A model endpoint named '" + request.Name + "' already exists.");
            ModelEndpoint endpoint = new ModelEndpoint();
            endpoint.TenantId = tenantId;
            Apply(endpoint, request);
            await ValidateDefinitionAsync(endpoint, token).ConfigureAwait(false);
            await _Database.ModelEndpoints.CreateAsync(endpoint, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, endpoint, "modelendpoint.created", "Created " + endpoint.Kind + " endpoint " + endpoint.Name + " (" + endpoint.Provider + " " + endpoint.Model + ").", token).ConfigureAwait(false);
            return endpoint;
        }

        /// <summary>
        /// Update an endpoint.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint.</returns>
        public async Task<ModelEndpoint> UpdateAsync(RequestContext ctx, string tenantId, string id, ModelEndpointRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            ModelEndpoint endpoint = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (request.Name != null && request.Name != endpoint.Name)
            {
                if (!_NamePattern.IsMatch(request.Name)) throw Invalid("name", "A name of 1 to 128 letters, digits, dots, hyphens, or underscores is required.");
                if (await _Database.ModelEndpoints.ReadByNameAsync(tenantId, request.Name, token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A model endpoint named '" + request.Name + "' already exists.");
            }
            Apply(endpoint, request);
            await ValidateDefinitionAsync(endpoint, token).ConfigureAwait(false);
            endpoint.HealthStatus = ModelHealthStatusEnum.Unknown;
            await _Database.ModelEndpoints.UpdateAsync(endpoint, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, endpoint, "modelendpoint.updated", "Updated model endpoint " + endpoint.Name + ".", token).ConfigureAwait(false);
            return endpoint;
        }

        /// <summary>
        /// Read an endpoint.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint.</returns>
        /// <exception cref="NotFoundException">Thrown when it does not exist in the tenant.</exception>
        public async Task<ModelEndpoint> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            return await _Database.ModelEndpoints.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("Model endpoint not found.");
        }

        /// <summary>
        /// Delete an endpoint (profiles referencing it lose the reference).
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted endpoint.</returns>
        public async Task<ModelEndpoint> DeleteAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            ModelEndpoint endpoint = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            await _Database.ModelEndpoints.DeleteAsync(tenantId, id, token).ConfigureAwait(false);
            await AfterChangeAsync(ctx, endpoint, "modelendpoint.deleted", "Deleted model endpoint " + endpoint.Name + ".", token).ConfigureAwait(false);
            return endpoint;
        }

        /// <summary>
        /// Build the runtime configuration of an endpoint, decrypting its API key from the vault. The key lives only in
        /// the returned object; do not keep it.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Configuration.</returns>
        public async Task<ModelEndpointConfig> ToConfigAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            ModelEndpointConfig config = new ModelEndpointConfig();
            config.Id = endpoint.Id;
            config.Name = endpoint.Name;
            config.Provider = endpoint.Provider;
            config.Kind = endpoint.Kind;
            config.BaseUrl = endpoint.BaseUrl;
            config.Model = endpoint.Model;
            config.Region = endpoint.Region;
            config.DeploymentName = endpoint.DeploymentName;
            config.ApiVersion = endpoint.ApiVersion;
            config.ContextWindowTokens = endpoint.ContextWindowTokens;
            config.MaxOutputTokens = endpoint.MaxOutputTokens;
            config.SupportsTools = endpoint.SupportsTools;
            config.SupportsStreaming = endpoint.SupportsStreaming;
            config.EmbeddingDimensions = endpoint.EmbeddingDimensions;
            config.MaxConcurrency = endpoint.MaxConcurrency;
            config.TimeoutSeconds = endpoint.TimeoutSeconds;
            config.MaxRetries = endpoint.MaxRetries;
            if (!String.IsNullOrEmpty(endpoint.ApiKeySecretId))
            {
                Secret secret = await _Secrets.ReadAsync(endpoint.TenantId, endpoint.ApiKeySecretId, token).ConfigureAwait(false);
                Dictionary<string, string> fields = await _Secrets.ResolveAsync(endpoint.TenantId, endpoint.ApiKeySecretId, token).ConfigureAwait(false);
                if (secret.Kind == SecretKindEnum.AccessKeyPair && String.IsNullOrEmpty(endpoint.ApiKeySecretField))
                {
                    config.AccessKeyId = fields.TryGetValue("accessKeyId", out string? id) ? id : null;
                    config.ApiKey = fields.TryGetValue("secretAccessKey", out string? key) ? key : null;
                }
                else
                {
                    string? field = String.IsNullOrEmpty(endpoint.ApiKeySecretField) ? SecretFieldRules.PrimaryField(secret.Kind) ?? fields.Keys.FirstOrDefault() : endpoint.ApiKeySecretField;
                    config.ApiKey = field != null && fields.TryGetValue(field, out string? value) ? value : null;
                }
            }
            return config;
        }

        /// <summary>
        /// Validate an endpoint by sending a tiny prompt (or embedding a short string) and record its health.
        /// </summary>
        /// <param name="ctx">Caller, or null for the health sweep.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validation result (errors scrubbed).</returns>
        public async Task<ModelValidationResult> ValidateAsync(RequestContext? ctx, string tenantId, string id, CancellationToken token = default)
        {
            ModelEndpoint endpoint = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            return await CheckAsync(endpoint, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Models the provider offers, where PolyPrompt can list them (Ollama, OpenAI, OpenAI-compatible, Anthropic,
        /// Gemini).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Model names.</returns>
        /// <exception cref="ConflictException">Thrown with code NotSupported when the provider cannot list models.</exception>
        public async Task<List<string>> ListProviderModelsAsync(string tenantId, string id, CancellationToken token = default)
        {
            ModelEndpoint endpoint = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            ModelEndpointConfig config = await ToConfigAsync(endpoint, token).ConfigureAwait(false);
            List<string> models = new List<string>();
            using (HttpClient http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
                ModelClientBase client;
                switch (config.Provider)
                {
                    case ModelProviderEnum.Ollama:
                        client = new OllamaModelClient(config.BaseUrl ?? "http://127.0.0.1:11434", config.ApiKey, _Logging, http);
                        break;
                    case ModelProviderEnum.OpenAI:
                    case ModelProviderEnum.OpenAICompatible:
                        client = new OpenAiModelClient(config.BaseUrl ?? "https://api.openai.com", config.ApiKey, _Logging, http);
                        break;
                    case ModelProviderEnum.Anthropic:
                        client = new AnthropicModelClient(config.BaseUrl ?? "https://api.anthropic.com", config.ApiKey, _Logging, http);
                        break;
                    case ModelProviderEnum.Gemini:
                        client = new GeminiModelClient(config.BaseUrl ?? "https://generativelanguage.googleapis.com", config.ApiKey ?? String.Empty, _Logging, http);
                        break;
                    default:
                        throw new ConflictException("NotSupported", "Listing models is not supported for " + config.Provider + " endpoints.");
                }
                using (client)
                {
                    await foreach (ModelInformation model in client.ListModelsAsync(token).ConfigureAwait(false))
                    {
                        if (!String.IsNullOrEmpty(model.Name)) models.Add(model.Name);
                        if (models.Count >= 1000) break;
                    }
                }
            }
            return models.Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Check every active endpoint once and record its health (P6.6).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints checked.</returns>
        public async Task<int> SweepAsync(CancellationToken token)
        {
            int checkedCount = 0;
            foreach (ModelEndpoint endpoint in await _Database.ModelEndpoints.ListActiveAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await CheckAsync(endpoint, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logging.Warn("[Models] health check of endpoint " + endpoint.Id + " failed: " + SecretScrubber.Shared.Scrub(e.Message));
                }
                checkedCount++;
            }
            return checkedCount;
        }

        #endregion

        #region Private-Methods

        private async Task<ModelValidationResult> CheckAsync(ModelEndpoint endpoint, CancellationToken token)
        {
            ModelValidationResult result;
            ModelEndpointConfig config = await ToConfigAsync(endpoint, token).ConfigureAwait(false);
            SecretScrubber scrubber = new SecretScrubber();
            scrubber.AddKnownValue(config.ApiKey);
            result = await _Gateway.ValidateAsync(config, token).ConfigureAwait(false);
            if (result.Error != null) result.Error = scrubber.Scrub(result.Error);
            ModelHealthStatusEnum previous = endpoint.HealthStatus;
            ModelHealthStatusEnum status = result.Success ? ModelHealthStatusEnum.Healthy : ModelHealthStatusEnum.Unhealthy;
            string message = result.Success ? "OK in " + result.LatencyMs + " ms" + (result.Dimensions.HasValue ? " (" + result.Dimensions.Value + " dimensions)" : String.Empty) : (result.Error ?? "Validation failed.");
            await _Database.ModelEndpoints.UpdateHealthAsync(endpoint.TenantId, endpoint.Id, status, message.Length > 2000 ? message.Substring(0, 2000) : message, token).ConfigureAwait(false);
            if (result.Success && endpoint.Kind == ModelKindEnum.Embedding && result.Dimensions.HasValue && endpoint.EmbeddingDimensions != result.Dimensions)
            {
                endpoint.EmbeddingDimensions = result.Dimensions;
                await _Database.ModelEndpoints.UpdateAsync(endpoint, token).ConfigureAwait(false);
            }
            HugoMetrics.Count(HugoMetricCatalog.ModelHealthChecks, 1, HugoMetrics.Tags("provider", endpoint.Provider.ToString(), "kind", endpoint.Kind.ToString(), "outcome", status == ModelHealthStatusEnum.Healthy ? "healthy" : "unhealthy"));
            if (previous != status) _Events?.Publish("modelendpoint.health", endpoint.TenantId, new { id = endpoint.Id, name = endpoint.Name, status = status.ToString() });
            return result;
        }

        private static void Apply(ModelEndpoint endpoint, ModelEndpointRequest request)
        {
            if (request.Name != null) endpoint.Name = request.Name.Trim();
            if (request.Kind.HasValue) endpoint.Kind = request.Kind.Value;
            if (request.Provider.HasValue) endpoint.Provider = request.Provider.Value;
            if (request.BaseUrl != null) endpoint.BaseUrl = Empty(request.BaseUrl);
            if (request.Model != null) endpoint.Model = request.Model.Trim();
            if (request.ApiKeySecretId != null) endpoint.ApiKeySecretId = Empty(request.ApiKeySecretId);
            if (request.ApiKeySecretField != null) endpoint.ApiKeySecretField = Empty(request.ApiKeySecretField);
            if (request.Region != null) endpoint.Region = Empty(request.Region);
            if (request.DeploymentName != null) endpoint.DeploymentName = Empty(request.DeploymentName);
            if (request.ApiVersion != null) endpoint.ApiVersion = Empty(request.ApiVersion);
            if (request.ContextWindowTokens.HasValue) endpoint.ContextWindowTokens = request.ContextWindowTokens.Value;
            if (request.MaxOutputTokens.HasValue) endpoint.MaxOutputTokens = request.MaxOutputTokens.Value;
            if (request.SupportsTools.HasValue) endpoint.SupportsTools = request.SupportsTools.Value;
            if (request.SupportsStreaming.HasValue) endpoint.SupportsStreaming = request.SupportsStreaming.Value;
            if (request.EmbeddingDimensions.HasValue) endpoint.EmbeddingDimensions = request.EmbeddingDimensions.Value <= 0 ? null : request.EmbeddingDimensions.Value;
            if (request.MaxConcurrency.HasValue) endpoint.MaxConcurrency = request.MaxConcurrency.Value;
            if (request.TimeoutSeconds.HasValue) endpoint.TimeoutSeconds = request.TimeoutSeconds.Value;
            if (request.MaxRetries.HasValue) endpoint.MaxRetries = request.MaxRetries.Value;
            if (request.Active.HasValue) endpoint.Active = request.Active.Value;
        }

        private async Task ValidateDefinitionAsync(ModelEndpoint endpoint, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (String.IsNullOrWhiteSpace(endpoint.Model)) errors["model"] = "A model is required.";
            string? unsupported = ModelClientFactory.UnsupportedReason(endpoint.Provider, endpoint.Kind);
            if (unsupported != null) errors["provider"] = unsupported;
            if (!String.IsNullOrEmpty(endpoint.BaseUrl) && !Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out Uri? _)) errors["baseUrl"] = "The base URL must be an absolute URL.";
            if (!String.IsNullOrEmpty(endpoint.ApiKeySecretId))
            {
                Secret? secret = await _Database.Secrets.ReadAsync(endpoint.TenantId, endpoint.ApiKeySecretId, token).ConfigureAwait(false);
                if (secret == null) errors["apiKeySecretId"] = "Secret " + endpoint.ApiKeySecretId + " does not exist in this tenant.";
                else if (!String.IsNullOrEmpty(endpoint.ApiKeySecretField) && !secret.Fields.Contains(endpoint.ApiKeySecretField, StringComparer.Ordinal)) errors["apiKeySecretField"] = "Secret " + secret.Name + " has no field '" + endpoint.ApiKeySecretField + "'.";
            }
            if (errors.Count > 0) throw new ValidationException("The model endpoint is invalid: " + String.Join(" ", errors.Values), errors);
        }

        private async Task AfterChangeAsync(RequestContext ctx, ModelEndpoint endpoint, string eventType, string description, CancellationToken token)
        {
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ModelEndpointChanged, "ModelEndpoint", endpoint.Id, description, endpoint.TenantId, token).ConfigureAwait(false);
            _Events?.Publish(eventType, endpoint.TenantId, new { id = endpoint.Id, name = endpoint.Name, kind = endpoint.Kind.ToString(), provider = endpoint.Provider.ToString() });
            EventHandler<string>? handler = Changed;
            if (handler == null) return;
            try
            {
                handler(this, endpoint.TenantId);
            }
            catch (Exception)
            {
            }
        }

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }

        #endregion
    }
}
