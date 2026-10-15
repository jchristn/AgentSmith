namespace Test.Automated
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

    /// <summary>
    /// A tiny OpenAI-compatible model on 127.0.0.1 (modeled on src/Test.Shared/Agent/StubLlm/StubLlmServer.cs) so the
    /// harness can run real agent turns without an external model. It answers POST .../chat/completions (streamed and
    /// not), GET .../models, and POST .../embeddings, always with the same reply and no tool calls. Thread-safe.
    /// </summary>
    public class StubLlmServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL, for example http://127.0.0.1:54321 (the endpoint base URL for an OpenAICompatible model endpoint).
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Model name reported by /models and in completions. Default stub-model.
        /// </summary>
        public string ModelName { get; set; } = "stub-model";

        /// <summary>
        /// Assistant reply for every chat completion.
        /// </summary>
        public string Reply { get; set; } = "Good evening, Mr. Anderson.";

        /// <summary>
        /// Embedding dimensions. Default 8.
        /// </summary>
        public int Dimensions { get; set; } = 8;

        /// <summary>
        /// Number of chat completion requests served.
        /// </summary>
        public int ChatRequests
        {
            get
            {
                return _ChatRequests;
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly Task _AcceptLoop;
        private int _ChatRequests = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening on a free loopback port.
        /// </summary>
        public StubLlmServer()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = "http://127.0.0.1:" + port;
            _Listener = new HttpListener();
            _Listener.Prefixes.Add(BaseUrl + "/");
            _Listener.Start();
            _AcceptLoop = Task.Run(() => AcceptLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stop listening.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
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
                _AcceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
            _Cts.Dispose();
        }

        #endregion

        #region Private-Methods

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
                if (context.Request.HttpMethod == "POST" && path.EndsWith("/chat/completions", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _ChatRequests);
                    StubChatRequest request = JsonSerializer.Deserialize<StubChatRequest>(body) ?? new StubChatRequest();
                    if (request.Stream) await StreamAsync(context, token).ConfigureAwait(false);
                    else await WriteJsonAsync(context, 200, Completion(), token).ConfigureAwait(false);
                }
                else if (context.Request.HttpMethod == "POST" && path.EndsWith("/embeddings", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, 200, Embeddings(body), token).ConfigureAwait(false);
                }
                else if (context.Request.HttpMethod == "GET" && path.EndsWith("/models", StringComparison.Ordinal))
                {
                    Dictionary<string, object?> model = new Dictionary<string, object?> { { "id", ModelName }, { "object", "model" }, { "owned_by", "stub" } };
                    await WriteJsonAsync(context, 200, new Dictionary<string, object?> { { "object", "list" }, { "data", new List<object?> { model } } }, token).ConfigureAwait(false);
                }
                else
                {
                    Dictionary<string, object?> error = new Dictionary<string, object?> { { "message", "not found" }, { "type", "not_found" } };
                    await WriteJsonAsync(context, 404, new Dictionary<string, object?> { { "error", error } }, token).ConfigureAwait(false);
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

        private Dictionary<string, object?> Usage()
        {
            return new Dictionary<string, object?> { { "prompt_tokens", 12 }, { "completion_tokens", 6 }, { "total_tokens", 18 } };
        }

        private Dictionary<string, object?> Completion()
        {
            Dictionary<string, object?> message = new Dictionary<string, object?> { { "role", "assistant" }, { "content", Reply } };
            Dictionary<string, object?> choice = new Dictionary<string, object?> { { "index", 0 }, { "message", message }, { "finish_reason", "stop" } };
            return new Dictionary<string, object?>
            {
                { "id", "chatcmpl-stub" },
                { "object", "chat.completion" },
                { "created", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "model", ModelName },
                { "choices", new List<object?> { choice } },
                { "usage", Usage() }
            };
        }

        private Dictionary<string, object?> Chunk(Dictionary<string, object?> delta, string? finishReason)
        {
            Dictionary<string, object?> choice = new Dictionary<string, object?> { { "index", 0 }, { "delta", delta }, { "finish_reason", finishReason } };
            return new Dictionary<string, object?>
            {
                { "id", "chatcmpl-stub" },
                { "object", "chat.completion.chunk" },
                { "created", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "model", ModelName },
                { "choices", new List<object?> { choice } }
            };
        }

        private async Task StreamAsync(HttpListenerContext context, CancellationToken token)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;
            Stream output = context.Response.OutputStream;
            bool first = true;
            for (int i = 0; i < Reply.Length; i += 8)
            {
                Dictionary<string, object?> delta = new Dictionary<string, object?> { { "content", Reply.Substring(i, Math.Min(8, Reply.Length - i)) } };
                if (first) delta["role"] = "assistant";
                first = false;
                await WriteSseAsync(output, JsonSerializer.Serialize(Chunk(delta, null)), token).ConfigureAwait(false);
            }
            await WriteSseAsync(output, JsonSerializer.Serialize(Chunk(new Dictionary<string, object?>(), "stop")), token).ConfigureAwait(false);
            Dictionary<string, object?> usage = new Dictionary<string, object?>
            {
                { "id", "chatcmpl-stub" },
                { "object", "chat.completion.chunk" },
                { "created", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "model", ModelName },
                { "choices", new List<object?>() },
                { "usage", Usage() }
            };
            await WriteSseAsync(output, JsonSerializer.Serialize(usage), token).ConfigureAwait(false);
            await WriteSseAsync(output, "[DONE]", token).ConfigureAwait(false);
            context.Response.Close();
        }

        private Dictionary<string, object?> Embeddings(string body)
        {
            List<string> inputs;
            try
            {
                inputs = (JsonSerializer.Deserialize<StubEmbeddingRequest>(body) ?? new StubEmbeddingRequest()).Input;
            }
            catch (JsonException)
            {
                inputs = new List<string> { body };
            }
            if (inputs.Count == 0) inputs.Add(String.Empty);
            List<object?> data = new List<object?>();
            for (int i = 0; i < inputs.Count; i++)
            {
                List<double> vector = Enumerable.Range(0, Dimensions).Select(d => (double)((inputs[i].Length + d) % 7) / 7.0).ToList();
                data.Add(new Dictionary<string, object?> { { "object", "embedding" }, { "index", i }, { "embedding", vector } });
            }
            return new Dictionary<string, object?>
            {
                { "object", "list" },
                { "model", ModelName },
                { "data", data },
                { "usage", new Dictionary<string, object?> { { "prompt_tokens", 4 }, { "total_tokens", 4 } } }
            };
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

        #endregion
    }
}
