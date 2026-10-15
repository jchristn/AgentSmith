namespace Test.Shared.Agent.StubLlm
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Inference;

    /// <summary>
    /// Deterministic stub LLM (P6.7): an in-process HTTP server on <c>127.0.0.1</c> that speaks enough of the OpenAI
    /// API for PolyPrompt's OpenAI-compatible clients, so the agent runtime, the model gateway, and benchmarks run
    /// against real HTTP without a model.
    /// <list type="bullet">
    /// <item><c>POST /v1/chat/completions</c>: answers from <see cref="Script"/>, streaming (SSE chunks, tool-call
    /// deltas, a usage chunk, then <c>[DONE]</c>) or not, with tool calls, text, separate reasoning, or an HTTP error
    /// with Retry-After.</item>
    /// <item><c>POST /v1/embeddings</c>: vectors from <see cref="Embedder"/> (feature hashing), string or array input.</item>
    /// <item><c>GET /v1/models</c>: one model, for connectivity probes.</item>
    /// </list>
    /// Usage: <c>using (StubLlmServer stub = new StubLlmServer()) { stub.Script.Then(StubLlmResponse.ToolCall("sql_query",
    /// "{...}"), StubLlmResponse.Message("done")); ModelEndpointConfig ep = stub.InferenceEndpoint(); ... }</c>. Every
    /// request is recorded in <see cref="Requests"/>. <see cref="LatencyMs"/> adds a fixed delay to every request.
    /// The server listens on a free port chosen at construction and stops on dispose. Thread-safe.
    /// </summary>
    public class StubLlmServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL, for example <c>http://127.0.0.1:50123</c>.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Port.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// The script answering chat requests. Never null.
        /// </summary>
        public StubLlmScript Script { get; set; } = new StubLlmScript();

        /// <summary>
        /// Embedder for the embeddings endpoint. Never null.
        /// </summary>
        public FeatureHashEmbedder Embedder { get; set; } = new FeatureHashEmbedder();

        /// <summary>
        /// Model name reported. Default <c>stub-model</c>.
        /// </summary>
        public string ModelName { get; set; } = "stub-model";

        /// <summary>
        /// Milliseconds added before every response. Default 0.
        /// </summary>
        public int LatencyMs { get; set; } = 0;

        /// <summary>
        /// Recorded requests, in arrival order.
        /// </summary>
        public List<StubRecordedRequest> Requests
        {
            get
            {
                lock (_Requests) return _Requests.ToList();
            }
        }

        /// <summary>
        /// Chat requests received.
        /// </summary>
        public List<StubRecordedRequest> ChatRequests
        {
            get
            {
                return Requests.Where(r => r.Path.EndsWith("/chat/completions", StringComparison.Ordinal)).ToList();
            }
        }

        /// <summary>
        /// Embedding requests received.
        /// </summary>
        public List<StubRecordedRequest> EmbeddingRequests
        {
            get
            {
                return Requests.Where(r => r.Path.EndsWith("/embeddings", StringComparison.Ordinal)).ToList();
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly List<StubRecordedRequest> _Requests = new List<StubRecordedRequest>();
        private readonly Task _AcceptLoop;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start a stub on a free loopback port.
        /// </summary>
        /// <param name="script">Script; null starts with an empty one.</param>
        public StubLlmServer(StubLlmScript? script = null)
        {
            if (script != null) Script = script;
            Port = FreePort();
            BaseUrl = "http://127.0.0.1:" + Port;
            _Listener = new HttpListener();
            _Listener.Prefixes.Add(BaseUrl + "/");
            _Listener.Start();
            _AcceptLoop = Task.Run(() => AcceptLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// An OpenAI-compatible inference endpoint pointing at this stub.
        /// </summary>
        /// <param name="id">Endpoint ID (gate and breaker key). Default derived from the port.</param>
        /// <returns>Endpoint.</returns>
        public ModelEndpointConfig InferenceEndpoint(string? id = null)
        {
            return new ModelEndpointConfig
            {
                Id = id ?? ("stub-" + Port),
                Name = "stub " + Port,
                Provider = ModelProviderEnum.OpenAICompatible,
                Kind = ModelKindEnum.Inference,
                BaseUrl = BaseUrl,
                Model = ModelName,
                ApiKey = "stub-key",
                ContextWindowTokens = 32768,
                MaxRetries = 0,
                TimeoutSeconds = 30
            };
        }

        /// <summary>
        /// An OpenAI-compatible embedding endpoint pointing at this stub.
        /// </summary>
        /// <param name="id">Endpoint ID. Default derived from the port.</param>
        /// <returns>Endpoint.</returns>
        public ModelEndpointConfig EmbeddingEndpoint(string? id = null)
        {
            return new ModelEndpointConfig
            {
                Id = id ?? ("stub-embed-" + Port),
                Name = "stub embeddings " + Port,
                Provider = ModelProviderEnum.OpenAICompatible,
                Kind = ModelKindEnum.Embedding,
                BaseUrl = BaseUrl,
                Model = "stub-embed",
                ApiKey = "stub-key",
                EmbeddingDimensions = Embedder.Dimensions,
                MaxRetries = 0,
                TimeoutSeconds = 30
            };
        }

        /// <summary>
        /// Stop the server.
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
            if (!disposing) return;
            try
            {
                _Cts.Cancel();
                _Listener.Stop();
                _Listener.Close();
            }
            catch (Exception)
            {
            }
            try
            {
                _AcceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
            _Cts.Dispose();
        }

        #endregion

        #region Private-Methods

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    if (token.IsCancellationRequested) return;
                    continue;
                }
                _ = Task.Run(() => HandleAsync(context, token));
            }
        }

        private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                string body;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                }
                if (LatencyMs > 0) await Task.Delay(LatencyMs, token).ConfigureAwait(false);

                if (context.Request.HttpMethod == "POST" && path.EndsWith("/chat/completions", StringComparison.Ordinal))
                {
                    await ChatAsync(context, path, body, token).ConfigureAwait(false);
                }
                else if (context.Request.HttpMethod == "POST" && path.EndsWith("/embeddings", StringComparison.Ordinal))
                {
                    await EmbeddingsAsync(context, path, body, token).ConfigureAwait(false);
                }
                else if (context.Request.HttpMethod == "GET" && path.EndsWith("/models", StringComparison.Ordinal))
                {
                    Dictionary<string, object?> model = new Dictionary<string, object?> { { "id", ModelName }, { "object", "model" }, { "owned_by", "stub" } };
                    await WriteJsonAsync(context, 200, new Dictionary<string, object?> { { "object", "list" }, { "data", new List<object?> { model } } }, token).ConfigureAwait(false);
                }
                else
                {
                    await WriteJsonAsync(context, 404, Error("not found", "not_found"), token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                try
                {
                    context.Response.Abort();
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task ChatAsync(HttpListenerContext context, string path, string body, CancellationToken token)
        {
            StubChatRequest request = JsonSerializer.Deserialize<StubChatRequest>(body) ?? new StubChatRequest();
            StubRecordedRequest recorded = new StubRecordedRequest
            {
                Path = path,
                Body = body,
                Model = request.Model,
                Stream = request.Stream,
                ToolChoice = request.ToolChoice,
                ToolNames = (request.Tools ?? new List<StubToolWire>()).Select(t => t.Function?.Name ?? String.Empty).ToList(),
                Messages = request.Messages ?? new List<StubChatMessage>()
            };
            lock (_Requests) _Requests.Add(recorded);

            StubLlmResponse response = Script.Next(recorded);
            if (response.DelayMs > 0) await Task.Delay(response.DelayMs, token).ConfigureAwait(false);

            if (response.StatusCode != 200)
            {
                if (response.RetryAfterSeconds.HasValue) context.Response.AddHeader("Retry-After", response.RetryAfterSeconds.Value.ToString());
                await WriteJsonAsync(context, response.StatusCode, Error(response.ErrorMessage, "stub_error"), token).ConfigureAwait(false);
                return;
            }

            List<StubToolCall> calls = response.ToolCalls.Select((c, i) => new StubToolCall
            {
                Id = String.IsNullOrEmpty(c.Id) ? "call_stub_" + Guid.NewGuid().ToString("N").Substring(0, 12) : c.Id,
                Name = c.Name,
                ArgumentsJson = c.ArgumentsJson
            }).ToList();
            string finishReason = calls.Count > 0 ? "tool_calls" : "stop";
            int promptTokens = response.PromptTokens ?? Math.Max(1, recorded.Messages.Sum(m => (m.Content ?? String.Empty).Length) / 4);
            int completionTokens = response.CompletionTokens ?? Math.Max(1, ((response.Text ?? String.Empty).Length + calls.Sum(c => c.Name.Length + c.ArgumentsJson.Length)) / 4);
            Dictionary<string, object?> usage = new Dictionary<string, object?>
            {
                { "prompt_tokens", promptTokens },
                { "completion_tokens", completionTokens },
                { "total_tokens", promptTokens + completionTokens }
            };
            string id = "chatcmpl-" + Guid.NewGuid().ToString("N").Substring(0, 16);
            long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!request.Stream)
            {
                Dictionary<string, object?> message = new Dictionary<string, object?> { { "role", "assistant" }, { "content", response.Text } };
                if (!String.IsNullOrEmpty(response.Reasoning)) message["reasoning_content"] = response.Reasoning;
                if (calls.Count > 0) message["tool_calls"] = calls.Select(c => (object?)WireCall(c, null, true)).ToList();
                Dictionary<string, object?> completion = new Dictionary<string, object?>
                {
                    { "id", id },
                    { "object", "chat.completion" },
                    { "created", created },
                    { "model", ModelName },
                    { "choices", new List<object?> { new Dictionary<string, object?> { { "index", 0 }, { "message", message }, { "finish_reason", finishReason } } } }
                };
                if (response.ReportUsage) completion["usage"] = usage;
                await WriteJsonAsync(context, 200, completion, token).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;
            Stream output = context.Response.OutputStream;
            bool first = true;

            if (!String.IsNullOrEmpty(response.Reasoning))
            {
                await SendChunkAsync(output, id, created, new Dictionary<string, object?> { { "role", "assistant" }, { "reasoning_content", response.Reasoning } }, null, response.ChunkDelayMs, token).ConfigureAwait(false);
                first = false;
            }
            string text = response.Text ?? String.Empty;
            int size = Math.Max(1, response.ChunkSize);
            for (int i = 0; i < text.Length; i += size)
            {
                Dictionary<string, object?> delta = new Dictionary<string, object?> { { "content", text.Substring(i, Math.Min(size, text.Length - i)) } };
                if (first) delta["role"] = "assistant";
                first = false;
                await SendChunkAsync(output, id, created, delta, null, response.ChunkDelayMs, token).ConfigureAwait(false);
            }
            for (int i = 0; i < calls.Count; i++)
            {
                StubToolCall c = calls[i];
                Dictionary<string, object?> head = new Dictionary<string, object?> { { "tool_calls", new List<object?> { WireCall(c, i, false) } } };
                if (first) head["role"] = "assistant";
                first = false;
                await SendChunkAsync(output, id, created, head, null, response.ChunkDelayMs, token).ConfigureAwait(false);
                string args = c.ArgumentsJson;
                int half = args.Length / 2;
                foreach (string piece in new string[] { args.Substring(0, half), args.Substring(half) })
                {
                    if (piece.Length == 0) continue;
                    Dictionary<string, object?> argDelta = new Dictionary<string, object?>
                    {
                        { "tool_calls", new List<object?> { new Dictionary<string, object?> { { "index", i }, { "function", new Dictionary<string, object?> { { "arguments", piece } } } } } }
                    };
                    await SendChunkAsync(output, id, created, argDelta, null, response.ChunkDelayMs, token).ConfigureAwait(false);
                }
            }
            await SendChunkAsync(output, id, created, new Dictionary<string, object?>(), finishReason, 0, token).ConfigureAwait(false);
            if (response.ReportUsage)
            {
                Dictionary<string, object?> usageChunk = new Dictionary<string, object?>
                {
                    { "id", id },
                    { "object", "chat.completion.chunk" },
                    { "created", created },
                    { "model", ModelName },
                    { "choices", new List<object?>() },
                    { "usage", usage }
                };
                await WriteSseAsync(output, JsonSerializer.Serialize(usageChunk), token).ConfigureAwait(false);
            }
            await WriteSseAsync(output, "[DONE]", token).ConfigureAwait(false);
            context.Response.Close();
        }

        private async Task EmbeddingsAsync(HttpListenerContext context, string path, string body, CancellationToken token)
        {
            StubEmbeddingRequest request = JsonSerializer.Deserialize<StubEmbeddingRequest>(body) ?? new StubEmbeddingRequest();
            StubRecordedRequest recorded = new StubRecordedRequest { Path = path, Body = body, Model = request.Model, Inputs = request.Input };
            lock (_Requests) _Requests.Add(recorded);

            List<object?> data = new List<object?>();
            for (int i = 0; i < request.Input.Count; i++)
            {
                data.Add(new Dictionary<string, object?> { { "object", "embedding" }, { "index", i }, { "embedding", Embedder.Embed(request.Input[i]) } });
            }
            int tokens = Math.Max(1, request.Input.Sum(s => s.Length) / 4);
            Dictionary<string, object?> result = new Dictionary<string, object?>
            {
                { "object", "list" },
                { "data", data },
                { "model", request.Model ?? "stub-embed" },
                { "usage", new Dictionary<string, object?> { { "prompt_tokens", tokens }, { "total_tokens", tokens } } }
            };
            await WriteJsonAsync(context, 200, result, token).ConfigureAwait(false);
        }

        private Dictionary<string, object?> WireCall(StubToolCall call, int? index, bool withArguments)
        {
            Dictionary<string, object?> function = new Dictionary<string, object?> { { "name", call.Name }, { "arguments", withArguments ? call.ArgumentsJson : String.Empty } };
            Dictionary<string, object?> wire = new Dictionary<string, object?> { { "id", call.Id }, { "type", "function" }, { "function", function } };
            if (index.HasValue) wire["index"] = index.Value;
            return wire;
        }

        private async Task SendChunkAsync(Stream output, string id, long created, Dictionary<string, object?> delta, string? finishReason, int delayMs, CancellationToken token)
        {
            if (delayMs > 0) await Task.Delay(delayMs, token).ConfigureAwait(false);
            Dictionary<string, object?> chunk = new Dictionary<string, object?>
            {
                { "id", id },
                { "object", "chat.completion.chunk" },
                { "created", created },
                { "model", ModelName },
                { "choices", new List<object?> { new Dictionary<string, object?> { { "index", 0 }, { "delta", delta }, { "finish_reason", finishReason } } } }
            };
            await WriteSseAsync(output, JsonSerializer.Serialize(chunk), token).ConfigureAwait(false);
        }

        private static async Task WriteSseAsync(Stream output, string data, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("data: " + data + "\n\n");
            await output.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false);
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, int status, object body, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            context.Response.Close();
        }

        private static Dictionary<string, object?> Error(string message, string type)
        {
            return new Dictionary<string, object?> { { "error", new Dictionary<string, object?> { { "message", message }, { "type", type } } } };
        }

        #endregion
    }
}
