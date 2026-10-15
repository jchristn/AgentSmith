namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Mcp;

    /// <summary>
    /// In-process fake of the parts of a Hugo server the CLI talks to, on 127.0.0.1 and a free port, built on
    /// <see cref="HttpListener"/>. It serves the token routes, token details, the agent profile list, and an MCP
    /// Streamable HTTP endpoint at <c>/mcp</c> and <c>/mcp/{profile}</c> that checks the bearer token and the session
    /// header, answers <c>initialize</c>, <c>ping</c>, <c>tools/list</c>, and <c>tools/call</c> (JSON and event-stream
    /// responses, a broken stream resumable with <c>Last-Event-ID</c>, a server-to-client request), serves the
    /// standalone GET stream once, and can reject the next MCP request with 401 or expire the session. It records
    /// every request. Thread-safe.
    /// </summary>
    public class FakeHugoServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base address, for example <c>http://127.0.0.1:50123</c>.
        /// </summary>
        public string BaseAddress { get; }

        /// <summary>
        /// Accepted access key.
        /// </summary>
        public string AccessKey { get; set; } = "ak-test";

        /// <summary>
        /// Accepted secret key.
        /// </summary>
        public string SecretKey { get; set; } = "sk-test-secret";

        /// <summary>
        /// Accepted email.
        /// </summary>
        public string Email { get; set; } = "anderson@example.com";

        /// <summary>
        /// Accepted password.
        /// </summary>
        public string Password { get; set; } = "red pill 42";

        /// <summary>
        /// Tenant ID reported for every principal.
        /// </summary>
        public string TenantId { get; set; } = "ten_fake";

        /// <summary>
        /// Whether <c>GET /v1.0/api/agent-profiles</c> exists. Default false (answers 404).
        /// </summary>
        public bool ProfilesRouteAvailable { get; set; } = false;

        /// <summary>
        /// Whether the next sign-in reports must-change-password, and token details say so too.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>
        /// Whether the standalone GET stream delivers one <c>notifications/tools/list_changed</c> (once); otherwise GET
        /// answers 405.
        /// </summary>
        public bool StandaloneStreamEnabled { get; set; } = false;

        /// <summary>
        /// Number of upcoming MCP POSTs to reject with 401 even with a valid token.
        /// </summary>
        public int RejectNextMcpRequests
        {
            get
            {
                return _RejectNext;
            }
            set
            {
                Interlocked.Exchange(ref _RejectNext, value);
            }
        }

        /// <summary>
        /// When true, the next MCP request with an ID that carries a session answers 404 and ends that session.
        /// </summary>
        public bool ExpireSessionOnNextRequest { get; set; } = false;

        /// <summary>
        /// Requests received, in order.
        /// </summary>
        public ConcurrentQueue<FakeHugoRequest> Requests { get; } = new ConcurrentQueue<FakeHugoRequest>();

        /// <summary>
        /// Number of tokens issued by <c>POST /v1.0/token</c>.
        /// </summary>
        public int TokensIssued
        {
            get
            {
                return _TokensIssued;
            }
        }

        /// <summary>
        /// Number of MCP sessions created.
        /// </summary>
        public int SessionsCreated
        {
            get
            {
                return _SessionsCreated;
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, bool> _Tokens = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _Sessions = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _Resumable = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _ClientAnswers = new ConcurrentDictionary<string, TaskCompletionSource<string>>(StringComparer.Ordinal);
        private int _TokensIssued = 0;
        private int _SessionsCreated = 0;
        private int _RejectNext = 0;
        private int _StandaloneSent = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening on 127.0.0.1 and a free port.
        /// </summary>
        public FakeHugoServer()
        {
            int port = FreePort();
            BaseAddress = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            _Listener = new HttpListener();
            _Listener.Prefixes.Add(BaseAddress + "/");
            _Listener.Start();
            Task.Run(() => AcceptLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Issue a valid token directly (as if signed in).
        /// </summary>
        /// <returns>Token.</returns>
        public string IssueToken()
        {
            string token = "tok-" + Guid.NewGuid().ToString("N");
            _Tokens[token] = true;
            return token;
        }

        /// <summary>
        /// Revoke every token (the next call with one gets 401).
        /// </summary>
        public void RevokeTokens()
        {
            _Tokens.Clear();
        }

        /// <summary>
        /// Whether a token is currently valid.
        /// </summary>
        /// <param name="token">Token.</param>
        /// <returns>True when valid.</returns>
        public bool IsValid(string? token)
        {
            return token != null && _Tokens.ContainsKey(token);
        }

        /// <summary>
        /// Requests matching a filter.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <returns>Matching requests.</returns>
        public List<FakeHugoRequest> Where(Func<FakeHugoRequest, bool> filter)
        {
            return Requests.ToArray().Where(filter).ToList();
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Cts.Cancel();
                try
                {
                    _Listener.Stop();
                    _Listener.Close();
                }
                catch (ObjectDisposedException)
                {
                }
                foreach (TaskCompletionSource<string> pending in _ClientAnswers.Values) pending.TrySetCanceled();
                _Cts.Dispose();
            }
            _Disposed = true;
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
                catch (Exception) when (token.IsCancellationRequested || !_Listener.IsListening)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    continue;
                }
                _ = Task.Run(() => HandleSafeAsync(context, token), CancellationToken.None);
            }
        }

        private async Task HandleSafeAsync(HttpListenerContext context, CancellationToken token)
        {
            try
            {
                await HandleAsync(context, token).ConfigureAwait(false);
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

        private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
        {
            HttpListenerRequest request = context.Request;
            FakeHugoRequest record = new FakeHugoRequest();
            record.Method = request.HttpMethod;
            record.RawPath = (request.RawUrl ?? "/").Split('?')[0];
            foreach (string? name in request.Headers.AllKeys)
            {
                if (name != null) record.Headers[name] = request.Headers[name] ?? String.Empty;
            }
            using (StreamReader reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                record.Body = await reader.ReadToEndAsync(token).ConfigureAwait(false);
            }
            Requests.Enqueue(record);

            string path = record.RawPath;
            if (path == "/v1.0/token" && record.Method == "POST") await IssueAsync(context, record).ConfigureAwait(false);
            else if (path == "/v1.0/token" && record.Method == "DELETE") await RevokeAsync(context, record).ConfigureAwait(false);
            else if (path == "/v1.0/token/details" && record.Method == "GET") await DetailsAsync(context, record).ConfigureAwait(false);
            else if (path == "/v1.0/api/agent-profiles" && record.Method == "GET") await ProfilesAsync(context, record).ConfigureAwait(false);
            else if (path == "/mcp" || path.StartsWith("/mcp/", StringComparison.Ordinal)) await McpAsync(context, record, token).ConfigureAwait(false);
            else await JsonAsync(context, record, 404, Error("NotFound", "No such route."), null).ConfigureAwait(false);
        }

        private async Task IssueAsync(HttpListenerContext context, FakeHugoRequest record)
        {
            string? accessKey = record.Header("x-access-key");
            string? secretKey = record.Header("x-secret-key");
            string? email = record.Header("x-email");
            string? password = record.Header("x-password");
            bool ok = (accessKey != null && accessKey == AccessKey && secretKey == SecretKey)
                || (accessKey == null && email != null && String.Equals(email, Email, StringComparison.OrdinalIgnoreCase) && password == Password);
            if (!ok)
            {
                await JsonAsync(context, record, 401, Error("InvalidCredentials", "The tenant, email, or password is invalid."), null).ConfigureAwait(false);
                return;
            }
            Interlocked.Increment(ref _TokensIssued);
            string token = IssueToken();
            string body = JsonSerializer.Serialize(new
            {
                token = token,
                expiresUtc = DateTime.UtcNow.AddHours(1),
                tenantId = TenantId,
                sessionId = "ses_" + _TokensIssued.ToString(CultureInfo.InvariantCulture),
                principalType = accessKey != null ? "Credential" : "User",
                mustChangePassword = accessKey == null && MustChangePassword
            });
            await JsonAsync(context, record, 200, body, null).ConfigureAwait(false);
        }

        private async Task RevokeAsync(HttpListenerContext context, FakeHugoRequest record)
        {
            string? bearer = Bearer(record);
            if (!IsValid(bearer))
            {
                await JsonAsync(context, record, 401, Error("Unauthorized", "Token invalid."), null).ConfigureAwait(false);
                return;
            }
            _Tokens.TryRemove(bearer!, out bool _);
            await JsonAsync(context, record, 200, "{\"valid\":false}", null).ConfigureAwait(false);
        }

        private async Task DetailsAsync(HttpListenerContext context, FakeHugoRequest record)
        {
            if (!IsValid(Bearer(record)))
            {
                await JsonAsync(context, record, 401, Error("Unauthorized", "Token invalid."), null).ConfigureAwait(false);
                return;
            }
            string body = JsonSerializer.Serialize(new
            {
                principalType = "User",
                principalId = "usr_anderson",
                principalName = "Thomas Anderson",
                tenantId = TenantId,
                userId = "usr_anderson",
                email = Email,
                isAdmin = false,
                isTenantAdmin = true,
                mustChangePassword = MustChangePassword,
                expiresUtc = DateTime.UtcNow.AddHours(1)
            });
            await JsonAsync(context, record, 200, body, null).ConfigureAwait(false);
        }

        private async Task ProfilesAsync(HttpListenerContext context, FakeHugoRequest record)
        {
            if (!IsValid(Bearer(record)))
            {
                await JsonAsync(context, record, 401, Error("Unauthorized", "Token invalid."), null).ConfigureAwait(false);
                return;
            }
            if (!ProfilesRouteAvailable)
            {
                await JsonAsync(context, record, 404, Error("NotFound", "No such route."), null).ConfigureAwait(false);
                return;
            }
            string body = JsonSerializer.Serialize(new
            {
                success = true,
                totalRecords = 2,
                objects = new object[]
                {
                    new { id = "apr_hugo", name = "Hugo", description = "Default profile (Agent Smith)." },
                    new { id = "apr_ops", name = "Ops Team", description = "Operations profile." }
                }
            });
            await JsonAsync(context, record, 200, body, null).ConfigureAwait(false);
        }

        private async Task McpAsync(HttpListenerContext context, FakeHugoRequest record, CancellationToken token)
        {
            if (record.Method == "POST" && TryConsumeReject())
            {
                await ChallengeAsync(context, record).ConfigureAwait(false);
                return;
            }
            if (!IsValid(Bearer(record)))
            {
                await ChallengeAsync(context, record).ConfigureAwait(false);
                return;
            }

            string? session = record.Header("MCP-Session-Id");
            if (record.Method == "DELETE")
            {
                if (session != null) _Sessions.TryRemove(session, out bool _);
                await StatusAsync(context, record, 200).ConfigureAwait(false);
                return;
            }
            if (record.Method == "GET")
            {
                await StandaloneAsync(context, record, session, token).ConfigureAwait(false);
                return;
            }
            if (record.Method != "POST")
            {
                await StatusAsync(context, record, 405).ConfigureAwait(false);
                return;
            }

            if (record.Body.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                await JsonAsync(context, record, 200, "[{\"jsonrpc\":\"2.0\",\"id\":\"b1\",\"result\":{}}]", null).ConfigureAwait(false);
                return;
            }

            JsonRpcMessage? message = JsonRpcMessage.TryParse(record.Body);
            if (message == null)
            {
                await JsonAsync(context, record, 400, "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error\"}}", null).ConfigureAwait(false);
                return;
            }
            record.RpcMethod = message.Method;
            bool stateless = record.Header("MCP-Protocol-Version") == "2026-07-28";

            if (message.IsRequest && message.Method == "initialize")
            {
                int number = Interlocked.Increment(ref _SessionsCreated);
                string created = "sess-" + number.ToString(CultureInfo.InvariantCulture);
                _Sessions[created] = true;
                string result = Result(message.Id, new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { tools = new { listChanged = true } },
                    serverInfo = new { name = "Hugo", version = "fake" },
                    instructions = "You are talking to a fake Hugo."
                });
                await JsonAsync(context, record, 200, result, new Dictionary<string, string> { { "MCP-Session-Id", created } }).ConfigureAwait(false);
                return;
            }

            if (!stateless)
            {
                if (session == null)
                {
                    await JsonAsync(context, record, 400, "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32600,\"message\":\"Missing session\"}}", null).ConfigureAwait(false);
                    return;
                }
                if (message.HasId && ExpireSessionOnNextRequest)
                {
                    ExpireSessionOnNextRequest = false;
                    _Sessions.TryRemove(session, out bool _);
                }
                if (!_Sessions.ContainsKey(session))
                {
                    await StatusAsync(context, record, 404).ConfigureAwait(false);
                    return;
                }
            }

            if (message.IsResponse)
            {
                string key = message.IdKey ?? String.Empty;
                if (_ClientAnswers.TryRemove(key, out TaskCompletionSource<string>? waiter)) waiter.TrySetResult(record.Body);
                await StatusAsync(context, record, 202).ConfigureAwait(false);
                return;
            }
            if (message.IsNotification)
            {
                await StatusAsync(context, record, 202).ConfigureAwait(false);
                return;
            }

            McpRequestParams? parameters = null;
            if (message.Params != null && message.Params.Value.ValueKind == JsonValueKind.Object)
                parameters = message.Params.Value.Deserialize<McpRequestParams>();

            switch (message.Method)
            {
                case "ping":
                    await JsonAsync(context, record, 200, Result(message.Id, new { }), null).ConfigureAwait(false);
                    return;
                case "tools/list":
                    await JsonAsync(context, record, 200, Result(message.Id, ToolList()), null).ConfigureAwait(false);
                    return;
                case "tools/call":
                    await ToolCallAsync(context, record, message, parameters, token).ConfigureAwait(false);
                    return;
                default:
                    await JsonAsync(context, record, 200, "{\"jsonrpc\":\"2.0\",\"id\":" + message.IdKey + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}", null).ConfigureAwait(false);
                    return;
            }
        }

        private async Task ToolCallAsync(HttpListenerContext context, FakeHugoRequest record, JsonRpcMessage message, McpRequestParams? parameters, CancellationToken token)
        {
            string name = parameters?.Name ?? String.Empty;
            string text = String.Empty;
            if (parameters?.Arguments != null && parameters.Arguments.TryGetValue("text", out JsonElement value) && value.ValueKind == JsonValueKind.String)
                text = value.GetString() ?? String.Empty;

            if (name == "echo" || name == "region_echo" || name == "café")
            {
                await JsonAsync(context, record, 200, Result(message.Id, TextResult("echo: " + text)), null).ConfigureAwait(false);
                return;
            }

            if (name == "stream_echo")
            {
                List<string> events = new List<string>();
                events.Add("id: s-0\ndata: \n\n");
                events.Add(": keep-alive comment\n\n");
                events.Add("id: s-1\nevent: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\ndata: \"params\":{\"progressToken\":\"p1\",\"progress\":1,\"total\":2}}\n\n");
                events.Add("id: s-2\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{\"level\":\"info\",\"data\":\"line one\\nline two\"}}\n\n");
                events.Add("id: s-3\ndata: " + Result(message.Id, TextResult("streamed: " + text)) + "\n\n");
                await SseAsync(context, record, events, null, token).ConfigureAwait(false);
                return;
            }

            if (name == "broken_stream")
            {
                string eventId = "b-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                _Resumable[eventId] = Result(message.Id, TextResult("resumed: " + text));
                List<string> events = new List<string>();
                events.Add("id: " + eventId + "\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progressToken\":\"b\",\"progress\":1}}\n\n");
                await SseAsync(context, record, events, null, token).ConfigureAwait(false);
                return;
            }

            if (name == "ask_client")
            {
                string requestId = "srv-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                TaskCompletionSource<string> waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ClientAnswers["\"" + requestId + "\""] = waiter;
                List<string> first = new List<string>();
                first.Add("id: a-1\ndata: {\"jsonrpc\":\"2.0\",\"id\":\"" + requestId + "\",\"method\":\"roots/list\"}\n\n");
                await SseAsync(context, record, first, async () =>
                {
                    Task finished = await Task.WhenAny(waiter.Task, Task.Delay(15000, token)).ConfigureAwait(false);
                    string answer = finished == waiter.Task ? waiter.Task.Result : "(no answer)";
                    JsonRpcMessage? reply = JsonRpcMessage.TryParse(answer);
                    string summary = reply != null && reply.Result != null ? "client answered" : "client did not answer";
                    return "id: a-2\ndata: " + Result(message.Id, TextResult(summary)) + "\n\n";
                }, token).ConfigureAwait(false);
                return;
            }

            await JsonAsync(context, record, 200, Result(message.Id, new { content = new object[] { new { type = "text", text = "unknown tool" } }, isError = true }), null).ConfigureAwait(false);
        }

        private async Task StandaloneAsync(HttpListenerContext context, FakeHugoRequest record, string? session, CancellationToken token)
        {
            string? lastEventId = record.Header("Last-Event-ID");
            if (lastEventId != null && _Resumable.TryRemove(lastEventId, out string? response))
            {
                await SseAsync(context, record, new List<string> { "id: r-1\ndata: " + response + "\n\n" }, null, token).ConfigureAwait(false);
                return;
            }
            if (!StandaloneStreamEnabled || session == null || !_Sessions.ContainsKey(session) || Interlocked.Exchange(ref _StandaloneSent, 1) != 0)
            {
                await StatusAsync(context, record, 405).ConfigureAwait(false);
                return;
            }
            await SseAsync(context, record, new List<string> { "id: g-1\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n" }, null, token).ConfigureAwait(false);
        }

        private bool TryConsumeReject()
        {
            while (true)
            {
                int current = _RejectNext;
                if (current <= 0) return false;
                if (Interlocked.CompareExchange(ref _RejectNext, current - 1, current) == current) return true;
            }
        }

        private static object ToolList()
        {
            Dictionary<string, object> regionProperty = new Dictionary<string, object> { { "type", "string" }, { "x-mcp-header", "Region" } };
            return new
            {
                tools = new object[]
                {
                    new { name = "echo", description = "Echo text.", inputSchema = new { type = "object", properties = new { text = new { type = "string" } } } },
                    new { name = "stream_echo", description = "Echo text over an event stream.", inputSchema = new { type = "object", properties = new { text = new { type = new string[] { "string", "null" } } } } },
                    new { name = "region_echo", description = "Echo with a header parameter.", inputSchema = new { type = "object", properties = new Dictionary<string, object> { { "region", regionProperty }, { "text", new { type = "string" } } } } }
                }
            };
        }

        private static object TextResult(string text)
        {
            return new { content = new object[] { new { type = "text", text = text } }, isError = false };
        }

        private static string Result(JsonElement? id, object result)
        {
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id, result = result });
        }

        private static string Error(string code, string message)
        {
            return JsonSerializer.Serialize(new { error = "Error", code = code, message = message });
        }

        private static string? Bearer(FakeHugoRequest record)
        {
            string? auth = record.Header("Authorization");
            if (auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal)) return null;
            return auth.Substring(7).Trim();
        }

        private static async Task ChallengeAsync(HttpListenerContext context, FakeHugoRequest record)
        {
            context.Response.AddHeader("WWW-Authenticate", "Bearer realm=\"hugo\"");
            await JsonAsync(context, record, 401, Error("Unauthorized", "Token invalid or expired."), null).ConfigureAwait(false);
        }

        private static async Task StatusAsync(HttpListenerContext context, FakeHugoRequest record, int status)
        {
            record.Status = status;
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = 0;
            context.Response.Close();
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task JsonAsync(HttpListenerContext context, FakeHugoRequest record, int status, string body, Dictionary<string, string>? headers)
        {
            record.Status = status;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers) context.Response.AddHeader(header.Key, header.Value);
            }
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private static async Task SseAsync(HttpListenerContext context, FakeHugoRequest record, List<string> events, Func<Task<string>>? tail, CancellationToken token)
        {
            record.Status = 200;
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;
            Stream output = context.Response.OutputStream;
            foreach (string item in events)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(item);
                await output.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            if (tail != null)
            {
                string last = await tail().ConfigureAwait(false);
                byte[] bytes = Encoding.UTF8.GetBytes(last);
                await output.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            context.Response.Close();
        }

        #endregion
    }
}
