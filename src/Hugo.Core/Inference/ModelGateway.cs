namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// Default <see cref="IModelGateway"/> over PolyPrompt (plan 6.4 and 13.1). For each endpoint in order (primary,
    /// then fallback): skip it when its circuit is open; otherwise acquire a concurrency slot, call it, and retry
    /// retryable failures (408, 429, 5xx, transport errors, timeouts) up to the endpoint's <c>MaxRetries</c> with
    /// backoff that honors Retry-After. A retryable failure that exhausts the retries counts toward the circuit.
    /// Streams are retried or failed over only before the first chunk reaches the caller; a stream that breaks
    /// later fails with <c>stream_interrupted</c>. Thread-safe.
    /// </summary>
    public class ModelGateway : IModelGateway
    {
        #region Public-Members

        /// <summary>
        /// Circuit breaker (exposed for health reporting and tests).
        /// </summary>
        public CircuitBreaker Breaker
        {
            get
            {
                return _Breaker;
            }
        }

        /// <summary>
        /// Concurrency gate (exposed for health reporting and tests).
        /// </summary>
        public EndpointGate Gate
        {
            get
            {
                return _Gate;
            }
        }

        /// <summary>
        /// Retry policy.
        /// </summary>
        public RetryPolicy Retry
        {
            get
            {
                return _Retry;
            }
        }

        #endregion

        #region Private-Members

        private readonly ModelClientFactory _Factory;
        private readonly ModelGatewaySettings _Settings;
        private readonly CircuitBreaker _Breaker;
        private readonly EndpointGate _Gate;
        private readonly RetryPolicy _Retry;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="factory">Client factory. The gateway does not dispose it.</param>
        /// <param name="settings">Settings; null uses defaults.</param>
        /// <param name="breaker">Circuit breaker; null creates one from the settings.</param>
        /// <param name="gate">Concurrency gate; null creates one.</param>
        /// <exception cref="ArgumentNullException">Thrown when factory is null.</exception>
        public ModelGateway(ModelClientFactory factory, ModelGatewaySettings? settings = null, CircuitBreaker? breaker = null, EndpointGate? gate = null)
        {
            _Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _Settings = settings ?? new ModelGatewaySettings();
            _Breaker = breaker ?? new CircuitBreaker { FailureThreshold = _Settings.CircuitFailureThreshold, OpenSeconds = _Settings.CircuitOpenSeconds };
            _Gate = gate ?? new EndpointGate();
            _Retry = new RetryPolicy
            {
                BaseDelayMs = _Settings.RetryBaseDelayMs,
                MaxDelayMs = _Settings.RetryMaxDelayMs,
                MaxRetryAfterSeconds = _Settings.MaxRetryAfterSeconds
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ModelChatResult> ChatAsync(ModelChatCall call, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(call);
            if (call.Messages.Count == 0) throw new ArgumentException("A chat call needs at least one message.", nameof(call));
            long start = Stopwatch.GetTimestamp();
            ModelAttemptCounter counter = new ModelAttemptCounter();
            ModelChatResult result = await RunAsync(call.Endpoint, call.Fallback, call.Kind, counter, (endpoint, t) => ChatAttemptAsync(endpoint, call, t), token).ConfigureAwait(false);
            result.Attempts = counter.Attempts;
            result.UsedFallback = counter.UsedFallback;
            result.DurationMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return result;
        }

        /// <inheritdoc />
        public async Task<ModelEmbedResult> EmbedAsync(ModelEmbedCall call, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(call);
            if (call.Inputs.Count == 0) throw new ArgumentException("An embedding call needs at least one input.", nameof(call));
            long start = Stopwatch.GetTimestamp();
            ModelAttemptCounter counter = new ModelAttemptCounter();
            ModelEmbedResult result = await RunAsync(call.Endpoint, call.Fallback, "embedding", counter, (endpoint, t) => EmbedAttemptAsync(endpoint, call.Inputs, t), token).ConfigureAwait(false);
            result.UsedFallback = counter.UsedFallback;
            result.DurationMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return result;
        }

        /// <inheritdoc />
        public async Task<ModelValidationResult> ValidateAsync(ModelEndpointConfig endpoint, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ModelValidationResult result = new ModelValidationResult { Kind = endpoint.Kind, Model = endpoint.Model };
            result.Problems = ModelClientFactory.Validate(endpoint);
            if (result.Problems.Count > 0)
            {
                result.Error = String.Join(" ", result.Problems);
                return result;
            }

            long start = Stopwatch.GetTimestamp();
            try
            {
                if (endpoint.Kind == ModelKindEnum.Embedding)
                {
                    using (EmbeddingClientBase client = _Factory.CreateEmbedding(endpoint))
                    {
                        EmbeddingResponse response = await client.EmbedAsync(_Settings.ValidationEmbedText, null, token).ConfigureAwait(false);
                        result.LatencyMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        result.StatusCode = response.StatusCode;
                        if (!response.Success || response.Embeddings.Count == 0)
                        {
                            result.Error = response.Error ?? "The endpoint returned no embedding.";
                            return result;
                        }
                        result.Dimensions = response.Embeddings[0].Embedding.Length;
                        if (!String.IsNullOrEmpty(response.Model)) result.Model = response.Model;
                        result.Success = result.Dimensions > 0;
                        if (!result.Success) result.Error = "The endpoint returned an empty vector.";
                    }
                }
                else
                {
                    using (CompletionClientBase client = _Factory.CreateCompletion(endpoint))
                    {
                        CompletionOptions options = new CompletionOptions { MaxTokens = 16, Temperature = 0 };
                        ChatResponse response = await client.ChatAsync(_Settings.ValidationPrompt, options, token).ConfigureAwait(false);
                        result.LatencyMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        result.StatusCode = response.StatusCode;
                        if (!response.Success)
                        {
                            result.Error = response.Error ?? "The endpoint returned an error.";
                            return result;
                        }
                        string text = response.Text ?? String.Empty;
                        result.ResponsePreview = text.Length > 80 ? text.Substring(0, 80) : text;
                        if (!String.IsNullOrEmpty(response.Model)) result.Model = response.Model;
                        result.Success = true;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                result.LatencyMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                result.Error = "The endpoint did not answer within " + endpoint.TimeoutSeconds + " seconds.";
            }
            catch (Exception e)
            {
                result.LatencyMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                result.Error = e.Message;
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task<T> RunAsync<T>(
            ModelEndpointConfig primary,
            ModelEndpointConfig? fallback,
            string kind,
            ModelAttemptCounter counter,
            Func<ModelEndpointConfig, CancellationToken, Task<T>> attempt,
            CancellationToken token)
        {
            List<ModelEndpointConfig> endpoints = new List<ModelEndpointConfig> { primary };
            if (fallback != null) endpoints.Add(fallback);
            ModelEndpointException? last = null;

            for (int i = 0; i < endpoints.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                ModelEndpointConfig endpoint = endpoints[i];
                string provider = endpoint.Provider.ToString();
                if (_Breaker.IsOpen(endpoint.Key))
                {
                    AgentTelemetry.RecordModelRequest(provider, kind, "circuit_open", 0);
                    last = new ModelEndpointException("circuit_open", endpoint.Key, "The circuit for model endpoint " + Describe(endpoint) + " is open for another " + _Breaker.SecondsUntilTrial(endpoint.Key).ToString("0.#") + " seconds after repeated failures.", null, true);
                    continue;
                }

                try
                {
                    T result = await WithRetriesAsync(endpoint, kind, counter, attempt, token).ConfigureAwait(false);
                    counter.UsedFallback = i > 0;
                    return result;
                }
                catch (ModelEndpointException e) when (e.Code == "stream_interrupted")
                {
                    throw;
                }
                catch (ModelEndpointException e)
                {
                    last = e;
                }
            }

            throw last ?? new ModelEndpointException("configuration", primary.Key, "No model endpoint was available.");
        }

        private async Task<T> WithRetriesAsync<T>(
            ModelEndpointConfig endpoint,
            string kind,
            ModelAttemptCounter counter,
            Func<ModelEndpointConfig, CancellationToken, Task<T>> attempt,
            CancellationToken token)
        {
            int maxAttempts = 1 + endpoint.MaxRetries;
            string provider = endpoint.Provider.ToString();
            for (int n = 1; ; n++)
            {
                ModelEndpointException? failure = null;
                long start = Stopwatch.GetTimestamp();
                Activity? span = AgentTelemetry.StartActivity("model " + provider.ToLowerInvariant() + " " + kind, ActivityKind.Client);
                AgentTelemetry.SetTag(span, "hugo.model.endpoint", endpoint.Key);
                AgentTelemetry.SetTag(span, "hugo.model.attempt", n);
                try
                {
                    using (EndpointGateLease lease = await _Gate.AcquireAsync(endpoint.Key, endpoint.MaxConcurrency, TimeSpan.FromSeconds(_Settings.QueueTimeoutSeconds), token).ConfigureAwait(false))
                    {
                        counter.Attempts++;
                        T result = await attempt(endpoint, token).ConfigureAwait(false);
                        _Breaker.RecordSuccess(endpoint.Key);
                        AgentTelemetry.RecordModelRequest(provider, kind, "success", Stopwatch.GetElapsedTime(start).TotalSeconds);
                        return result;
                    }
                }
                catch (ModelEndpointException e)
                {
                    failure = e;
                    AgentTelemetry.RecordModelRequest(provider, kind, e.Code, Stopwatch.GetElapsedTime(start).TotalSeconds);
                    AgentTelemetry.SetError(span, e.Code);
                }
                catch (OperationCanceledException)
                {
                    AgentTelemetry.RecordModelRequest(provider, kind, "cancelled", Stopwatch.GetElapsedTime(start).TotalSeconds);
                    throw;
                }
                finally
                {
                    AgentTelemetry.Stop(span);
                }

                bool retry = failure.Retryable
                    && failure.Code != "queue_timeout"
                    && n < maxAttempts
                    && _Retry.AcceptsRetryAfter(failure.RetryAfterSeconds);
                if (!retry)
                {
                    if (failure.Retryable && failure.Code != "queue_timeout") _Breaker.RecordFailure(endpoint.Key);
                    throw failure;
                }

                await Task.Delay(_Retry.ComputeDelay(n, failure.RetryAfterSeconds), token).ConfigureAwait(false);
            }
        }

        private async Task<ModelChatResult> ChatAttemptAsync(ModelEndpointConfig endpoint, ModelChatCall call, CancellationToken token)
        {
            CompletionClientBase client = CreateCompletion(endpoint);
            using (client)
            {
                ToolChatRequest request = BuildRequest(endpoint, call);
                ModelCallContext context = new ModelCallContext();
                ModelCallContext.Current = context;
                long start = Stopwatch.GetTimestamp();
                bool streaming = call.Stream && endpoint.SupportsStreaming;
                ModelChatResult result = new ModelChatResult
                {
                    EndpointKey = endpoint.Key,
                    Provider = endpoint.Provider,
                    Model = endpoint.Model
                };
                TokenUsage? usage;

                if (streaming)
                {
                    ToolChatStreamingResponse response;
                    try
                    {
                        response = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        throw Classify(endpoint, e, false);
                    }
                    if (!response.Success) throw HttpFailure(endpoint, response.StatusCode, response.Error, context);

                    bool delivered = false;
                    try
                    {
                        await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(token).ConfigureAwait(false))
                        {
                            bool content = !String.IsNullOrEmpty(chunk.Text) || !String.IsNullOrEmpty(chunk.ReasoningText) || (chunk.ToolCallDeltas != null && chunk.ToolCallDeltas.Count > 0);
                            if (!content) continue;
                            if (result.TimeToFirstTokenMs < 0)
                            {
                                result.TimeToFirstTokenMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                                AgentTelemetry.RecordTimeToFirstToken(endpoint.Provider.ToString(), result.TimeToFirstTokenMs / 1000.0);
                            }
                            delivered = true;
                            if (call.OnChunk != null) await call.OnChunk(chunk, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        throw Classify(endpoint, e, delivered);
                    }

                    result.Text = response.Text ?? String.Empty;
                    result.Reasoning = response.Reasoning;
                    result.ToolCalls = response.ToolCalls != null ? response.ToolCalls.ToList() : new List<ToolCall>();
                    result.FinishReason = response.FinishReason;
                    if (!String.IsNullOrEmpty(response.Model)) result.Model = response.Model;
                    usage = response.Usage;
                }
                else
                {
                    ToolChatResponse response;
                    try
                    {
                        response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        throw Classify(endpoint, e, false);
                    }
                    if (!response.Success) throw HttpFailure(endpoint, response.StatusCode, response.Error, context);

                    result.TimeToFirstTokenMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    result.Text = response.Text ?? String.Empty;
                    result.Reasoning = response.Reasoning;
                    result.ToolCalls = response.ToolCalls != null ? response.ToolCalls.ToList() : new List<ToolCall>();
                    result.FinishReason = response.FinishReason;
                    if (!String.IsNullOrEmpty(response.Model)) result.Model = response.Model;
                    usage = response.Usage;
                    if (call.OnChunk != null && (!String.IsNullOrEmpty(result.Text) || !String.IsNullOrEmpty(result.Reasoning)))
                    {
                        ToolChatStreamingChunk whole = new ToolChatStreamingChunk { Text = result.Text, ReasoningText = result.Reasoning, Done = true };
                        await call.OnChunk(whole, token).ConfigureAwait(false);
                    }
                }

                if (usage != null && (usage.PromptTokens.HasValue || usage.CompletionTokens.HasValue))
                {
                    result.InputTokens = usage.PromptTokens ?? 0;
                    result.OutputTokens = usage.CompletionTokens ?? 0;
                }
                else
                {
                    result.TokensEstimated = true;
                    result.InputTokens = EstimateInput(request);
                    result.OutputTokens = EstimateOutput(result);
                }
                AgentTelemetry.RecordTokens(endpoint.Provider.ToString(), result.InputTokens, result.OutputTokens);
                return result;
            }
        }

        private async Task<ModelEmbedResult> EmbedAttemptAsync(ModelEndpointConfig endpoint, List<string> inputs, CancellationToken token)
        {
            EmbeddingClientBase client = CreateEmbedding(endpoint);
            using (client)
            {
                ModelCallContext context = new ModelCallContext();
                ModelCallContext.Current = context;
                EmbeddingResponse response;
                try
                {
                    response = await client.EmbedAsync(inputs, null, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw Classify(endpoint, e, false);
                }
                if (!response.Success) throw HttpFailure(endpoint, response.StatusCode, response.Error, context);
                if (response.Embeddings.Count != inputs.Count)
                    throw new ModelEndpointException("bad_response", endpoint.Key, "The embedding endpoint returned " + response.Embeddings.Count + " vectors for " + inputs.Count + " inputs.");

                ModelEmbedResult result = new ModelEmbedResult { EndpointKey = endpoint.Key };
                result.Vectors = response.Embeddings.OrderBy(e => e.Index).Select(e => e.Embedding).ToList();
                result.Dimensions = result.Vectors.Count > 0 ? result.Vectors[0].Length : 0;
                return result;
            }
        }

        private CompletionClientBase CreateCompletion(ModelEndpointConfig endpoint)
        {
            try
            {
                return _Factory.CreateCompletion(endpoint);
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                throw new ModelEndpointException("configuration", endpoint.Key, "Model endpoint " + Describe(endpoint) + " is misconfigured: " + e.Message, null, false, null, e);
            }
        }

        private EmbeddingClientBase CreateEmbedding(ModelEndpointConfig endpoint)
        {
            try
            {
                return _Factory.CreateEmbedding(endpoint);
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                throw new ModelEndpointException("configuration", endpoint.Key, "Model endpoint " + Describe(endpoint) + " is misconfigured: " + e.Message, null, false, null, e);
            }
        }

        private static ToolChatRequest BuildRequest(ModelEndpointConfig endpoint, ModelChatCall call)
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages = call.Messages;
            bool tools = endpoint.SupportsTools && call.Tools.Count > 0;
            request.Tools = tools ? call.Tools : new List<ToolDefinition>();
            request.ToolChoice = tools ? (call.ToolChoice ?? "auto") : null;
            request.Options = new CompletionOptions
            {
                MaxTokens = call.MaxOutputTokens ?? endpoint.MaxOutputTokens,
                Temperature = call.Temperature ?? endpoint.Temperature
            };
            return request;
        }

        private static ModelEndpointException HttpFailure(ModelEndpointConfig endpoint, int? statusCode, string? error, ModelCallContext context)
        {
            int? status = statusCode ?? context.LastStatusCode;
            string detail = Truncate(error ?? "no detail", 500);
            if (status.HasValue && status.Value >= 200 && status.Value < 300)
                return new ModelEndpointException("bad_response", endpoint.Key, "Model endpoint " + Describe(endpoint) + " returned an unusable response: " + detail, status, false);
            if (!status.HasValue)
                return new ModelEndpointException("transport_error", endpoint.Key, "Model endpoint " + Describe(endpoint) + " could not be reached: " + detail, null, true);
            bool retryable = RetryPolicy.IsRetryableStatus(status);
            return new ModelEndpointException("http_error", endpoint.Key, "Model endpoint " + Describe(endpoint) + " returned HTTP " + status.Value + ": " + detail, status, retryable, context.RetryAfterSeconds);
        }

        private static ModelEndpointException Classify(ModelEndpointConfig endpoint, Exception e, bool delivered)
        {
            if (delivered)
                return new ModelEndpointException("stream_interrupted", endpoint.Key, "The stream from model endpoint " + Describe(endpoint) + " broke after output began: " + e.Message, null, false, null, e);
            if (e is OperationCanceledException)
                return new ModelEndpointException("timeout", endpoint.Key, "Model endpoint " + Describe(endpoint) + " did not answer within " + endpoint.TimeoutSeconds + " seconds.", null, true, null, e);
            return new ModelEndpointException("transport_error", endpoint.Key, "Model endpoint " + Describe(endpoint) + " failed: " + e.Message, null, true, null, e);
        }

        private static string Describe(ModelEndpointConfig endpoint)
        {
            return String.IsNullOrEmpty(endpoint.Name) ? endpoint.Key : endpoint.Name;
        }

        private static long EstimateInput(ToolChatRequest request)
        {
            long chars = 0;
            foreach (ChatMessage m in request.Messages)
            {
                chars += (m.Content ?? String.Empty).Length + 16;
                if (m.ToolCalls != null) chars += m.ToolCalls.Sum(c => c.Name.Length + (c.ArgumentsJson ?? String.Empty).Length);
            }
            if (request.Tools != null) chars += request.Tools.Sum(t => t.Name.Length + t.Description.Length + 200);
            return (chars + 3) / 4;
        }

        private static long EstimateOutput(ModelChatResult result)
        {
            long chars = result.Text.Length + (result.Reasoning ?? String.Empty).Length;
            chars += result.ToolCalls.Sum(c => c.Name.Length + (c.ArgumentsJson ?? String.Empty).Length);
            return (chars + 3) / 4;
        }

        private static string Truncate(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }

        #endregion
    }
}
