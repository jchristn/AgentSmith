namespace Hugo.Cli.Mcp
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Api;

    /// <summary>
    /// Relays the MCP stdio transport to the Streamable HTTP transport. Each line of standard input is one JSON-RPC
    /// message; it is posted to the MCP endpoint with <c>Authorization: Bearer</c>, the session header, and the
    /// protocol version headers, and whatever the server answers (a JSON body or an event stream with notifications,
    /// progress, server requests, and the response) is written to standard output, one compact message per line.
    /// Nothing else is ever written to standard output; diagnostics go to the diagnostic writer (standard error).
    /// <para>
    /// Requests run concurrently, so a long tool call never blocks later messages; <c>initialize</c>, notifications,
    /// and responses to server requests are forwarded in order. A 401 renews the session token once (when an access
    /// key is available). A 404 on a session revision starts a new session by replaying the client's
    /// <c>initialize</c>. A broken response stream is resumed with <c>Last-Event-ID</c>, or re-issued under the
    /// stateless revision. When standard input ends, requests in flight get a grace period, the session is deleted,
    /// and <see cref="RunAsync"/> returns.
    /// </para>
    /// Thread-safe; <see cref="RunAsync"/> may be called once.
    /// </summary>
    public class McpStdioRelay : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Settings.
        /// </summary>
        public McpRelaySettings Settings { get; }

        /// <summary>
        /// Token holder and renewer.
        /// </summary>
        public RelayAuthenticator Authenticator { get; }

        /// <summary>
        /// The MCP session ID the server assigned, or null (no session yet, or a stateless server).
        /// </summary>
        public string? SessionId
        {
            get
            {
                return _SessionId;
            }
        }

        /// <summary>
        /// The protocol version negotiated by <c>initialize</c>, or null.
        /// </summary>
        public string? ProtocolVersion
        {
            get
            {
                return _ProtocolVersion;
            }
        }

        /// <summary>
        /// Messages written to standard output.
        /// </summary>
        public long MessagesWritten
        {
            get
            {
                return _Output?.MessagesWritten ?? 0;
            }
        }

        #endregion

        #region Private-Members

        private const string _SessionHeader = "MCP-Session-Id";
        private const string _VersionHeader = "MCP-Protocol-Version";
        private const string _MethodHeader = "Mcp-Method";
        private const string _NameHeader = "Mcp-Name";
        private const string _ParamHeaderPrefix = "Mcp-Param-";

        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private readonly TextWriter _Diagnostics;
        private readonly ConcurrentDictionary<long, Task> _InFlight = new ConcurrentDictionary<long, Task>();
        private readonly ConcurrentDictionary<string, List<McpHeaderParameter>> _ToolHeaders = new ConcurrentDictionary<string, List<McpHeaderParameter>>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _ReinitializeLock = new SemaphoreSlim(1, 1);
        private ProtocolOutputWriter? _Output = null;
        private CancellationTokenSource? _Lifetime = null;
        private Task? _Listener = null;
        private JsonElement? _InitializeParams = null;
        private volatile string? _SessionId = null;
        private volatile string? _ProtocolVersion = null;
        private volatile string? _StatelessVersion = null;
        private long _NextTaskId = 0;
        private int _Started = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="authenticator">Token holder.</param>
        /// <param name="diagnostics">Diagnostic writer (standard error). Never standard output.</param>
        /// <param name="httpClient">HTTP client, or null to create one without a timeout (tool calls may run long) that this instance disposes.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings, authenticator, or diagnostics is null.</exception>
        public McpStdioRelay(McpRelaySettings settings, RelayAuthenticator authenticator, TextWriter diagnostics, HttpClient? httpClient = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
            _Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            if (httpClient == null)
            {
                _Http = new HttpClient();
                _Http.Timeout = Timeout.InfiniteTimeSpan;
                _OwnsHttp = true;
            }
            else
            {
                _Http = httpClient;
                _OwnsHttp = false;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Relay until the input ends or the token is cancelled.
        /// </summary>
        /// <param name="input">Standard input.</param>
        /// <param name="output">Standard output. Only protocol messages are written to it.</param>
        /// <param name="token">Cancellation token (cancel to stop, for example on Ctrl+C).</param>
        /// <returns>Exit code: 0.</returns>
        /// <exception cref="ArgumentNullException">Thrown when input or output is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when called more than once.</exception>
        public async Task<int> RunAsync(Stream input, Stream output, CancellationToken token)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (Interlocked.Exchange(ref _Started, 1) != 0) throw new InvalidOperationException("The relay has already run.");

            _Lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Output = new ProtocolOutputWriter(output);
            CancellationToken lifetime = _Lifetime.Token;
            Verbose("Relaying stdio to " + Settings.Endpoint);

            try
            {
                using (StreamReader reader = new StreamReader(input, new UTF8Encoding(false), false, 65536, true))
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        string? line;
                        try
                        {
                            line = await reader.ReadLineAsync(lifetime).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (IOException e)
                        {
                            Log("Standard input failed: " + e.Message);
                            break;
                        }
                        if (line == null) break;
                        if (String.IsNullOrWhiteSpace(line)) continue;
                        await DispatchLineAsync(line.Trim(), lifetime).ConfigureAwait(false);
                    }
                }
                Verbose("Standard input closed.");
            }
            finally
            {
                await ShutdownAsync().ConfigureAwait(false);
            }
            return 0;
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
                _Lifetime?.Cancel();
                _Lifetime?.Dispose();
                _Output?.Dispose();
                _ReinitializeLock.Dispose();
                if (_OwnsHttp) _Http.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task DispatchLineAsync(string line, CancellationToken token)
        {
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                if (TryMinify(line) == null)
                {
                    await WriteAsync(JsonRpcMessage.ErrorJson(null, -32700, "Parse error: the line is not valid JSON.", null), token).ConfigureAwait(false);
                    return;
                }
                RelayExchange batch = new RelayExchange();
                batch.Body = line;
                batch.IsBatch = true;
                Track(ForwardSafeAsync(batch, token));
                return;
            }

            JsonRpcMessage? message = JsonRpcMessage.TryParse(line);
            if (message == null)
            {
                Log("Ignoring a line that is not a JSON-RPC object.");
                await WriteAsync(JsonRpcMessage.ErrorJson(null, -32700, "Parse error: the line is not a JSON-RPC message.", null), token).ConfigureAwait(false);
                return;
            }

            RelayExchange exchange = RelayExchange.FromMessage(line, message);
            if (exchange.StatelessVersion != null) _StatelessVersion = exchange.StatelessVersion;
            if (exchange.IsInitialize && message.Params != null) _InitializeParams = message.Params;
            Verbose("-> " + exchange.Describe);

            if (message.IsRequest && !exchange.IsInitialize)
            {
                Track(ForwardSafeAsync(exchange, token));
                return;
            }

            await ForwardSafeAsync(exchange, token).ConfigureAwait(false);

            if (message.IsNotification
                && String.Equals(message.Method, "notifications/initialized", StringComparison.Ordinal)
                && Settings.ListenForServerMessages
                && _SessionId != null
                && _Listener == null)
            {
                _Listener = Task.Run(() => ListenAsync(token), CancellationToken.None);
            }
        }

        private void Track(Task task)
        {
            long id = Interlocked.Increment(ref _NextTaskId);
            _InFlight[id] = task;
            task.ContinueWith(t => _InFlight.TryRemove(id, out Task? _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task ForwardSafeAsync(RelayExchange exchange, CancellationToken token)
        {
            try
            {
                await ForwardAsync(exchange, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                await FailAsync(exchange, "The relay failed to deliver " + exchange.Describe + ": " + e.Message, null, token).ConfigureAwait(false);
            }
        }

        private async Task ForwardAsync(RelayExchange exchange, CancellationToken token)
        {
            bool renewed = false;
            bool reinitialized = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string? bearer = await Authenticator.GetTokenAsync(token).ConfigureAwait(false);
                string? session = exchange.StatelessVersion == null ? _SessionId : null;

                HttpResponseMessage response;
                using (HttpRequestMessage request = BuildPost(exchange, bearer, session))
                {
                    try
                    {
                        response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    }
                    catch (HttpRequestException e)
                    {
                        await FailAsync(exchange, "Cannot reach the Hugo MCP endpoint " + Settings.Endpoint + ": " + e.Message, 0, token).ConfigureAwait(false);
                        return;
                    }
                    catch (TaskCanceledException e) when (!token.IsCancellationRequested)
                    {
                        await FailAsync(exchange, "The Hugo MCP endpoint did not answer: " + e.Message, 0, token).ConfigureAwait(false);
                        return;
                    }
                }

                using (response)
                {
                    int status = (int)response.StatusCode;
                    if (status == 401 && !renewed)
                    {
                        renewed = true;
                        if (await Authenticator.RenewAsync(bearer, token).ConfigureAwait(false)) continue;
                    }
                    if (status == 401)
                    {
                        await FailAsync(exchange, "Hugo rejected the credentials (HTTP 401). Run 'hugo login', or check HUGO_TOKEN, HUGO_ACCESS_KEY, and HUGO_SECRET_KEY.", 401, token).ConfigureAwait(false);
                        return;
                    }
                    if (status == 404 && session != null && !exchange.IsInitialize && !exchange.Internal && !reinitialized && _InitializeParams != null)
                    {
                        reinitialized = true;
                        Log("The MCP session ended on the server (HTTP 404); starting a new session.");
                        if (await ReinitializeAsync(session, token).ConfigureAwait(false)) continue;
                        await FailAsync(exchange, "The MCP session ended on the server and a new one could not be started.", 404, token).ConfigureAwait(false);
                        return;
                    }
                    await RelayResponseAsync(exchange, response, token).ConfigureAwait(false);
                    return;
                }
            }
        }

        private HttpRequestMessage BuildPost(RelayExchange exchange, string? bearer, string? session)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Settings.Endpoint);
            request.Content = new StringContent(exchange.Body, new UTF8Encoding(false), "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.TryAddWithoutValidation("User-Agent", ProductInfo.UserAgent);
            if (!String.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (!String.IsNullOrEmpty(session)) request.Headers.TryAddWithoutValidation(_SessionHeader, session);

            string? version = exchange.StatelessVersion;
            if (version == null && !exchange.IsInitialize) version = _ProtocolVersion ?? (_SessionId == null ? _StatelessVersion : null);
            if (!String.IsNullOrEmpty(version)) request.Headers.TryAddWithoutValidation(_VersionHeader, version);

            if (exchange.StatelessVersion != null && exchange.Message != null && exchange.Message.IsRequest && !String.IsNullOrEmpty(exchange.Method))
            {
                request.Headers.TryAddWithoutValidation(_MethodHeader, exchange.Method);
                if (exchange.RoutingName != null) request.Headers.TryAddWithoutValidation(_NameHeader, McpHeaderEncoder.Encode(exchange.RoutingName));
                if (String.Equals(exchange.Method, "tools/call", StringComparison.Ordinal)) AddParamHeaders(request, exchange);
            }
            return request;
        }

        private void AddParamHeaders(HttpRequestMessage request, RelayExchange exchange)
        {
            if (exchange.RoutingName == null || exchange.Arguments == null) return;
            if (!_ToolHeaders.TryGetValue(exchange.RoutingName, out List<McpHeaderParameter>? parameters)) return;
            foreach (McpHeaderParameter parameter in parameters)
            {
                if (!exchange.Arguments.TryGetValue(parameter.PropertyName, out JsonElement value)) continue;
                string? text = McpHeaderEncoder.FormatArgument(value, parameter.Type);
                if (text != null) request.Headers.TryAddWithoutValidation(_ParamHeaderPrefix + parameter.HeaderName, text);
            }
        }

        private async Task RelayResponseAsync(RelayExchange exchange, HttpResponseMessage response, CancellationToken token)
        {
            int status = (int)response.StatusCode;
            if (exchange.IsInitialize && response.IsSuccessStatusCode)
            {
                if (response.Headers.TryGetValues(_SessionHeader, out IEnumerable<string>? values))
                {
                    string? session = values.FirstOrDefault();
                    _SessionId = String.IsNullOrEmpty(session) ? null : session;
                }
                else
                {
                    _SessionId = null;
                }
            }

            if (status == 202 || status == 204)
            {
                if (exchange.ExpectsResponse)
                    await FailAsync(exchange, "The Hugo MCP endpoint accepted " + exchange.Describe + " without answering it (HTTP " + status + ").", status, token).ConfigureAwait(false);
                return;
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (String.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                await RelayStreamAsync(exchange, response, token).ConfigureAwait(false);
                return;
            }

            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(body))
            {
                if (!response.IsSuccessStatusCode)
                    await FailAsync(exchange, "The Hugo MCP endpoint answered HTTP " + status + " " + response.ReasonPhrase + ".", status, token).ConfigureAwait(false);
                else if (exchange.ExpectsResponse)
                    await FailAsync(exchange, "The Hugo MCP endpoint answered " + exchange.Describe + " with an empty body.", status, token).ConfigureAwait(false);
                return;
            }

            string? compact = TryMinify(body);
            if (compact != null && IsJsonRpc(compact))
            {
                if (!response.IsSuccessStatusCode) Verbose("HTTP " + status + " for " + exchange.Describe);
                bool answered = await EmitAsync(exchange, compact, token).ConfigureAwait(false);
                if (!answered && exchange.ExpectsResponse && !compact.StartsWith("[", StringComparison.Ordinal))
                    await FailAsync(exchange, "The Hugo MCP endpoint answered " + exchange.Describe + " with a message for another request.", status, token).ConfigureAwait(false);
                return;
            }

            ApiErrorResponse? error = CliJson.TryDeserialize<ApiErrorResponse>(compact);
            string detail = !String.IsNullOrEmpty(error?.Message) ? error!.Message! : Truncate(body.Trim(), 200);
            await FailAsync(exchange, "The Hugo MCP endpoint answered HTTP " + status + ": " + detail, status, token).ConfigureAwait(false);
        }

        private async Task RelayStreamAsync(RelayExchange exchange, HttpResponseMessage response, CancellationToken token)
        {
            SseRelayState state = new SseRelayState();
            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            {
                await PumpAsync(exchange, stream, state, token).ConfigureAwait(false);
            }

            int attempts = 0;
            while (!state.GotResponse && exchange.ExpectsResponse && !token.IsCancellationRequested)
            {
                if (exchange.StatelessVersion == null && _SessionId != null && state.LastEventId != null && attempts < Settings.MaxResumeAttempts)
                {
                    attempts++;
                    Log("The response stream for " + exchange.Describe + " ended early; resuming after event " + state.LastEventId + ".");
                    if (!await ResumeAsync(exchange, state, token).ConfigureAwait(false)) break;
                    continue;
                }
                if (exchange.StatelessVersion != null && !exchange.Reissued)
                {
                    Log("The response stream for " + exchange.Describe + " ended early; re-issuing the request.");
                    await ForwardAsync(exchange.Reissue(), token).ConfigureAwait(false);
                    return;
                }
                break;
            }

            if (!state.GotResponse && exchange.ExpectsResponse && !token.IsCancellationRequested)
                await FailAsync(exchange, "The response stream from Hugo ended before the response to " + exchange.Describe + " arrived.", (int)response.StatusCode, token).ConfigureAwait(false);
        }

        private async Task<bool> ResumeAsync(RelayExchange exchange, SseRelayState state, CancellationToken token)
        {
            if (state.RetryMs != null && state.RetryMs.Value > 0)
                await Task.Delay(Math.Min(state.RetryMs.Value, 30000), token).ConfigureAwait(false);
            string? bearer = await Authenticator.GetTokenAsync(token).ConfigureAwait(false);
            try
            {
                using (HttpRequestMessage request = BuildGet(bearer, _SessionId, state.LastEventId))
                {
                    using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        string? mediaType = response.Content.Headers.ContentType?.MediaType;
                        if (!response.IsSuccessStatusCode || !String.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                        {
                            Log("Resuming the stream failed (HTTP " + (int)response.StatusCode + ").");
                            return false;
                        }
                        using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                        {
                            await PumpAsync(exchange, stream, state, token).ConfigureAwait(false);
                        }
                        return true;
                    }
                }
            }
            catch (HttpRequestException e)
            {
                Log("Resuming the stream failed: " + e.Message);
                return false;
            }
        }

        private async Task PumpAsync(RelayExchange? exchange, Stream stream, SseRelayState state, CancellationToken token)
        {
            using (SseEventReader reader = new SseEventReader(stream))
            {
                try
                {
                    while (true)
                    {
                        SseEvent? item = await reader.ReadNextAsync(token).ConfigureAwait(false);
                        if (item == null) break;
                        if (item.Id != null) state.LastEventId = item.Id;
                        if (item.RetryMs != null) state.RetryMs = item.RetryMs;
                        if (String.IsNullOrWhiteSpace(item.Data)) continue;
                        string? compact = TryMinify(item.Data);
                        if (compact == null)
                        {
                            Log("Skipping an event whose data is not JSON.");
                            continue;
                        }
                        bool answered = exchange != null
                            ? await EmitAsync(exchange, compact, token).ConfigureAwait(false)
                            : await EmitUnsolicitedAsync(compact, token).ConfigureAwait(false);
                        state.MessagesRelayed++;
                        if (answered)
                        {
                            state.GotResponse = true;
                            break;
                        }
                    }
                }
                catch (IOException e)
                {
                    Verbose("Event stream broke: " + e.Message);
                }
                catch (HttpRequestException e)
                {
                    Verbose("Event stream broke: " + e.Message);
                }
            }
        }

        private async Task<bool> EmitAsync(RelayExchange exchange, string compact, CancellationToken token)
        {
            JsonRpcMessage? message = compact.StartsWith("{", StringComparison.Ordinal) ? JsonRpcMessage.TryParse(compact) : null;
            bool answered = message != null && message.IsResponse && exchange.IdKey != null && String.Equals(message.IdKey, exchange.IdKey, StringComparison.Ordinal);
            if (answered)
            {
                if (exchange.IsInitialize && message!.Result != null)
                {
                    McpInitializeResult? initialized = CliJson.TryDeserialize<McpInitializeResult>(message.Result);
                    if (!String.IsNullOrEmpty(initialized?.ProtocolVersion)) _ProtocolVersion = initialized!.ProtocolVersion;
                }
                if (String.Equals(exchange.Method, "tools/list", StringComparison.Ordinal) && message!.Result != null) LearnToolHeaders(message.Result.Value);
                if (exchange.Internal) return true;
                if (exchange.Reissued && exchange.OriginalId != null)
                {
                    message!.Id = exchange.OriginalId;
                    compact = message.ToJson();
                }
                Verbose("<- response " + (exchange.OriginalId?.GetRawText() ?? exchange.IdKey));
            }
            else
            {
                Verbose("<- " + (message?.Method ?? (message != null ? "response " + message.IdKey : "message")));
            }
            await WriteAsync(compact, token).ConfigureAwait(false);
            return answered;
        }

        private async Task<bool> EmitUnsolicitedAsync(string compact, CancellationToken token)
        {
            Verbose("<- server message");
            await WriteAsync(compact, token).ConfigureAwait(false);
            return false;
        }

        private void LearnToolHeaders(JsonElement result)
        {
            McpToolsListResult? list = CliJson.TryDeserialize<McpToolsListResult>(result);
            if (list?.Tools == null) return;
            foreach (McpToolSummary tool in list.Tools)
            {
                if (String.IsNullOrEmpty(tool.Name)) continue;
                List<McpHeaderParameter> parameters = new List<McpHeaderParameter>();
                if (tool.InputSchema?.Properties != null)
                {
                    foreach (KeyValuePair<string, McpSchemaProperty> property in tool.InputSchema.Properties)
                    {
                        if (property.Value == null || String.IsNullOrWhiteSpace(property.Value.Header)) continue;
                        McpHeaderParameter parameter = new McpHeaderParameter();
                        parameter.PropertyName = property.Key;
                        parameter.HeaderName = property.Value.Header!.Trim();
                        parameter.Type = property.Value.Type;
                        parameters.Add(parameter);
                    }
                }
                if (parameters.Count > 0) _ToolHeaders[tool.Name] = parameters;
                else _ToolHeaders.TryRemove(tool.Name, out List<McpHeaderParameter>? _);
            }
        }

        private async Task<bool> ReinitializeAsync(string expiredSession, CancellationToken token)
        {
            await _ReinitializeLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_SessionId != null && !String.Equals(_SessionId, expiredSession, StringComparison.Ordinal)) return true;
                _SessionId = null;

                JsonRpcMessage initialize = new JsonRpcMessage();
                initialize.JsonRpc = "2.0";
                initialize.Id = JsonSerializer.SerializeToElement("hugo-cli-initialize-" + Guid.NewGuid().ToString("N"));
                initialize.Method = "initialize";
                initialize.Params = _InitializeParams;
                RelayExchange exchange = RelayExchange.FromMessage(initialize.ToJson(), initialize);
                exchange.Internal = true;
                await ForwardAsync(exchange, token).ConfigureAwait(false);
                if (_SessionId == null) return false;

                JsonRpcMessage initialized = new JsonRpcMessage();
                initialized.JsonRpc = "2.0";
                initialized.Method = "notifications/initialized";
                RelayExchange notification = RelayExchange.FromMessage(initialized.ToJson(), initialized);
                notification.Internal = true;
                await ForwardAsync(notification, token).ConfigureAwait(false);
                Log("Started a new MCP session.");
                return true;
            }
            finally
            {
                _ReinitializeLock.Release();
            }
        }

        private HttpRequestMessage BuildGet(string? bearer, string? session, string? lastEventId)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Settings.Endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.TryAddWithoutValidation("User-Agent", ProductInfo.UserAgent);
            if (!String.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (!String.IsNullOrEmpty(session)) request.Headers.TryAddWithoutValidation(_SessionHeader, session);
            if (!String.IsNullOrEmpty(_ProtocolVersion)) request.Headers.TryAddWithoutValidation(_VersionHeader, _ProtocolVersion);
            if (!String.IsNullOrEmpty(lastEventId)) request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
            return request;
        }

        private async Task ListenAsync(CancellationToken token)
        {
            int failures = 0;
            bool renewed = false;
            string? lastEventId = null;
            while (!token.IsCancellationRequested && failures < Settings.MaxListenFailures)
            {
                string? session = _SessionId;
                if (session == null) return;
                int delay = Settings.ReconnectDelayMs;
                try
                {
                    string? bearer = await Authenticator.GetTokenAsync(token).ConfigureAwait(false);
                    using (HttpRequestMessage request = BuildGet(bearer, session, lastEventId))
                    {
                        using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                        {
                            int status = (int)response.StatusCode;
                            string? mediaType = response.Content.Headers.ContentType?.MediaType;
                            if (status == 405 || status == 404)
                            {
                                Verbose("The server offers no stream for server-initiated messages (HTTP " + status + ").");
                                return;
                            }
                            if (status == 401)
                            {
                                if (!renewed && await Authenticator.RenewAsync(bearer, token).ConfigureAwait(false))
                                {
                                    renewed = true;
                                    continue;
                                }
                                Log("The stream for server-initiated messages was refused (HTTP 401).");
                                return;
                            }
                            if (!response.IsSuccessStatusCode || !String.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                            {
                                failures++;
                                Verbose("The stream for server-initiated messages failed (HTTP " + status + ").");
                            }
                            else
                            {
                                renewed = false;
                                SseRelayState state = new SseRelayState();
                                state.LastEventId = lastEventId;
                                using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                                {
                                    await PumpAsync(null, stream, state, token).ConfigureAwait(false);
                                }
                                lastEventId = state.LastEventId;
                                if (state.RetryMs != null) delay = Math.Max(50, Math.Min(state.RetryMs.Value, 60000));
                                if (state.MessagesRelayed > 0) failures = 0;
                                else failures++;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpRequestException e)
                {
                    failures++;
                    Verbose("The stream for server-initiated messages failed: " + e.Message);
                }
                catch (IOException e)
                {
                    failures++;
                    Verbose("The stream for server-initiated messages failed: " + e.Message);
                }

                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task ShutdownAsync()
        {
            Task[] pending = _InFlight.Values.ToArray();
            if (pending.Length > 0 && Settings.ShutdownGraceMs > 0)
            {
                Verbose("Waiting for " + pending.Length + " request(s) in flight.");
                await Task.WhenAny(Task.WhenAll(pending), Task.Delay(Settings.ShutdownGraceMs)).ConfigureAwait(false);
            }

            try
            {
                _Lifetime?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            pending = _InFlight.Values.ToArray();
            if (pending.Length > 0) await Task.WhenAny(Task.WhenAll(pending), Task.Delay(1000)).ConfigureAwait(false);
            if (_Listener != null) await Task.WhenAny(_Listener, Task.Delay(1000)).ConfigureAwait(false);

            string? session = _SessionId;
            if (Settings.DeleteSessionOnExit && session != null)
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    try
                    {
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, Settings.Endpoint))
                        {
                            string? bearer = Authenticator.CurrentToken;
                            request.Headers.TryAddWithoutValidation("User-Agent", ProductInfo.UserAgent);
                            if (!String.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                            request.Headers.TryAddWithoutValidation(_SessionHeader, session);
                            if (!String.IsNullOrEmpty(_ProtocolVersion)) request.Headers.TryAddWithoutValidation(_VersionHeader, _ProtocolVersion);
                            using (HttpResponseMessage response = await _Http.SendAsync(request, cts.Token).ConfigureAwait(false))
                            {
                                Verbose("Deleted the MCP session (HTTP " + (int)response.StatusCode + ").");
                            }
                        }
                    }
                    catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException)
                    {
                        Verbose("Could not delete the MCP session: " + e.Message);
                    }
                }
            }
        }

        private async Task FailAsync(RelayExchange exchange, string message, int? httpStatus, CancellationToken token)
        {
            Log(message);
            if (exchange.Internal || exchange.Message == null || !exchange.Message.IsRequest) return;
            if (token.IsCancellationRequested) return;
            JsonElement? id = exchange.OriginalId ?? exchange.Message.Id;
            try
            {
                await WriteAsync(JsonRpcMessage.ErrorJson(id, -32603, message, httpStatus), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task WriteAsync(string compact, CancellationToken token)
        {
            if (_Output == null) return;
            try
            {
                await _Output.WriteAsync(compact, token).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                Log("Standard output failed: " + e.Message);
                try
                {
                    _Lifetime?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static string? TryMinify(string json)
        {
            try
            {
                return CliJson.Minify(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsJsonRpc(string compact)
        {
            if (compact.StartsWith("[", StringComparison.Ordinal)) return true;
            JsonRpcMessage? message = JsonRpcMessage.TryParse(compact);
            return message != null && String.Equals(message.JsonRpc, "2.0", StringComparison.Ordinal);
        }

        private static string Truncate(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }

        private void Log(string message)
        {
            lock (_Diagnostics)
            {
                try
                {
                    _Diagnostics.WriteLine("[hugo mcp stdio] " + message);
                    _Diagnostics.Flush();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private void Verbose(string message)
        {
            if (Settings.Verbose) Log(message);
        }

        #endregion
    }
}
