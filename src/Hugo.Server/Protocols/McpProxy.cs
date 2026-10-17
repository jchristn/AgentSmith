namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// The public MCP endpoint at /mcp and /mcp/{profileName} (P7.1, D-005), adapted from Garrison's McpClusterProxy.
    /// Voltaic's MCP server listens on an internal loopback port; this proxy runs inside the Watson listener, so REST,
    /// MCP, and A2A share one public port. Each request is authenticated here once with the REST schemes (signed-request
    /// nonces are used once), its agent profile is resolved (D-018: path, x-hugo-profile, credential, user, tenant), and
    /// the caller is handed to the internal endpoint through a per-request handle protected by a per-process secret.
    /// Unauthenticated requests get 401 with a bearer challenge. Responses are relayed unchanged: event streams chunk by
    /// chunk, everything else buffered and sent with a length (the Garrison framing fix). Thread-safe.
    /// </summary>
    public class McpProxy : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Header carrying the per-process secret that marks a request to the internal endpoint as coming from the proxy.
        /// </summary>
        public static readonly string ProxySecretHeader = "x-hugo-mcp-proxy-secret";

        /// <summary>
        /// Header carrying the caller handle of a proxied request.
        /// </summary>
        public static readonly string ProxyHandleHeader = "x-hugo-mcp-proxy-handle";

        /// <summary>
        /// Header carrying the session principal of a proxied request.
        /// </summary>
        public static readonly string ProxyPrincipalHeader = "x-hugo-mcp-proxy-principal";

        /// <summary>
        /// Header carrying the client address of a proxied request.
        /// </summary>
        public static readonly string ProxyClientHeader = "x-hugo-mcp-proxy-client";

        /// <summary>
        /// Header naming the agent profile to act as (D-018), when the path does not.
        /// </summary>
        public static readonly string ProfileHeader = "x-hugo-profile";

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _HopHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "Content-Length", "Expect", "Proxy-Connection", "TE", "Trailer", "Upgrade", "Date", "Server"
        };

        private static readonly HashSet<string> _CredentialHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "x-token", "x-api-key", "x-access-key", "x-secret-key", "x-signature", "x-nonce", "x-date", "x-email", "x-password", "x-hugo-chat-session"
        };

        private static readonly HashSet<string> _KnownMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "initialize", "notifications/initialized", "ping", "server/discover", "subscriptions/listen", "tools/list", "tools/call",
            "resources/list", "resources/templates/list", "resources/read", "resources/subscribe", "resources/unsubscribe",
            "prompts/list", "prompts/get", "completion/complete", "logging/setLevel", "notifications/cancelled", "notifications/progress"
        };

        private readonly ServerContext _Context;
        private readonly HugoMcpServer _Server;
        private readonly string _InternalUrl;
        private readonly string _Secret;
        private readonly HttpClient _Http;
        private readonly string _Header = "[McpProxy] ";
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="server">MCP server that owns the internal endpoint.</param>
        /// <param name="internalPort">Loopback port of the internal endpoint.</param>
        /// <param name="internalPath">Path of the internal endpoint (for example /mcp).</param>
        /// <param name="secret">Per-process secret shared with the internal endpoint.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public McpProxy(ServerContext context, HugoMcpServer server, int internalPort, string internalPath, string secret)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            if (String.IsNullOrEmpty(secret)) throw new ArgumentNullException(nameof(secret));
            if (String.IsNullOrEmpty(internalPath)) throw new ArgumentNullException(nameof(internalPath));
            _Secret = secret;
            _InternalUrl = "http://127.0.0.1:" + internalPort + internalPath;

            SocketsHttpHandler handler = new SocketsHttpHandler();
            handler.AllowAutoRedirect = false;
            handler.UseCookies = false;
            handler.UseProxy = false;
            handler.AutomaticDecompression = DecompressionMethods.None;
            handler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1);
            _Http = new HttpClient(handler);
            _Http.Timeout = Timeout.InfiniteTimeSpan;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle one request to /mcp or /mcp/{profileName}.
        /// </summary>
        /// <param name="http">HTTP context.</param>
        /// <param name="pathProfile">Profile named by the path, or null for /mcp.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when http is null.</exception>
        public async Task HandleAsync(HttpContextBase http, string? pathProfile)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            string verb = http.Request.Method.ToString().ToUpperInvariant();
            byte[]? body = verb == "POST" ? http.Request.DataAsBytes : null;
            McpRequestEnvelope? envelope = Parse(body);
            string method = MethodLabel(verb, envelope);
            string outcome = "error";
            string? handle = null;
            Activity? span = HugoTracing.Start("mcp " + method, ActivityKind.Server);
            span?.SetTag("rpc.system", "jsonrpc");
            span?.SetTag("rpc.method", method);
            try
            {
                if (!_Context.Settings.Mcp.Enable)
                {
                    outcome = "not_found";
                    await SendErrorAsync(http, 404, envelope?.Id, -32601, "McpDisabled: the MCP surface is disabled on this server.").ConfigureAwait(false);
                    return;
                }

                RequestContext ctx = await _Context.Authentication.ResolveAsync(http.Request.Headers, verb, http.Request.Url.RawWithoutQuery, Query(http.Request.Url.RawWithQuery), body, http.Request.Source.IpAddress, http.Token).ConfigureAwait(false);
                if (!ctx.IsAuthenticated)
                {
                    outcome = "unauthenticated";
                    bool presented = ctx.AuthResult != AuthResultEnum.NotFound || !String.IsNullOrEmpty(http.Request.Headers["Authorization"]);
                    http.Response.Headers.Add("WWW-Authenticate", "Bearer realm=\"hugo\"" + (presented ? ", error=\"invalid_token\", error_description=\"The supplied credentials are invalid or expired.\"" : String.Empty));
                    await SendErrorAsync(http, 401, envelope?.Id, -32001, "Unauthorized: authentication is required (Authorization: Bearer, or x-access-key with x-secret-key).").ConfigureAwait(false);
                    return;
                }
                http.Metadata = ctx;
                span?.SetTag("hugo.principal_type", ctx.PrincipalType.ToString());

                if (String.IsNullOrEmpty(ctx.TenantId))
                {
                    outcome = "bad_request";
                    await SendErrorAsync(http, 400, envelope?.Id, -32602, "TenantRequired: no tenant could be determined; system administrators must send x-tenant-guid.").ConfigureAwait(false);
                    return;
                }

                McpCaller? caller = await ResolveCallerAsync(http, ctx, pathProfile, envelope).ConfigureAwait(false);
                if (caller == null)
                {
                    outcome = http.Response.StatusCode == 404 ? "not_found" : "forbidden";
                    return;
                }
                span?.SetTag("hugo.profile", caller.Profile.Name);

                handle = _Server.RegisterCaller(caller);
                if (envelope != null && String.Equals(envelope.Method, "tools/call", StringComparison.Ordinal))
                {
                    await _Server.PrepareToolCallAsync(caller, envelope.Params?.Name, http.Token).ConfigureAwait(false);
                }

                using (HttpResponseMessage response = await ForwardAsync(verb, http, body, caller, handle).ConfigureAwait(false))
                {
                    outcome = await RelayAsync(http, response).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                outcome = "cancelled";
            }
            catch (Exception e) when (e is HttpRequestException || e is IOException)
            {
                _Context.Logging.Warn(_Header + "internal MCP endpoint failed: " + e.Message);
                if (!http.Response.ResponseSent) await SendErrorAsync(http, 502, envelope?.Id, -32603, "McpUnavailable: the MCP endpoint is not available.").ConfigureAwait(false);
            }
            catch (Exception e) when (DatabaseFailures.Classify(e) != DatabaseFailureEnum.None)
            {
                DatabaseFailureEnum failure = DatabaseFailures.Classify(e);
                _Context.Logging.Warn(_Header + "database " + failure.ToString().ToLowerInvariant() + " while handling an MCP request: " + e.Message);
                if (!http.Response.ResponseSent)
                {
                    http.Response.Headers.Add("Retry-After", Math.Max(1, _Context.Settings.Rest.DatabaseRetryAfterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    await SendErrorAsync(http, 503, envelope?.Id, -32603, (failure == DatabaseFailureEnum.Busy ? "DatabaseBusy" : "DatabaseUnavailable") + ": the database is not available right now; retry later.").ConfigureAwait(false);
                }
            }
            finally
            {
                if (handle != null) _Server.ReleaseCaller(handle);
                // A client closing a long-lived stream (subscriptions/listen, GET) is normal, not a failure.
                if (outcome == "error") HugoTracing.Fail(span, outcome);
                else HugoTracing.Ok(span);
                span?.SetTag("hugo.outcome", outcome);
                span?.Dispose();
                HugoMetrics.Count(HugoMetricCatalog.McpRequests, "method", method, "outcome", outcome);
            }
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
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Http.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<McpCaller?> ResolveCallerAsync(HttpContextBase http, RequestContext ctx, string? pathProfile, McpRequestEnvelope? envelope)
        {
            string tenantId = ctx.TenantId!;
            string? explicitProfile = !String.IsNullOrWhiteSpace(pathProfile) ? pathProfile : http.Request.Headers[ProfileHeader];
            if (String.IsNullOrWhiteSpace(explicitProfile)) explicitProfile = null;
            ProfileResolution resolution;
            try
            {
                resolution = await _Context.Get<ProfileResolver>().ResolveAsync(ctx, tenantId, explicitProfile, http.Token).ConfigureAwait(false);
            }
            catch (NotFoundException e)
            {
                await SendErrorAsync(http, 404, envelope?.Id, -32602, "ProfileNotFound: " + e.Message).ConfigureAwait(false);
                return null;
            }
            catch (ForbiddenException e)
            {
                await SendErrorAsync(http, 403, envelope?.Id, -32003, e.Code + ": " + e.Message).ConfigureAwait(false);
                return null;
            }

            AgentProfile profile = resolution.Profile;
            AuthorizationDecision decision = await _Context.Authorization.AuthorizeAsync(ctx, tenantId, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, profile.Id, http.Token).ConfigureAwait(false);
            if (!decision.Permitted)
            {
                await SendErrorAsync(http, 403, envelope?.Id, -32003, "Forbidden: the caller may not use agent profile " + profile.Name + " (Execute on AgentProfile is required).").ConfigureAwait(false);
                return null;
            }
            if (!profile.ExposeMcp)
            {
                await SendErrorAsync(http, 403, envelope?.Id, -32003, "McpNotExposed: agent profile " + profile.Name + " is not exposed over MCP.").ConfigureAwait(false);
                return null;
            }

            McpCaller caller = new McpCaller();
            caller.Principal = ctx;
            caller.TenantId = tenantId;
            caller.Profile = profile;
            caller.ProfileSource = resolution.Source;
            caller.ExplicitProfile = explicitProfile;
            return caller;
        }

        private async Task<HttpResponseMessage> ForwardAsync(string verb, HttpContextBase http, byte[]? body, McpCaller caller, string handle)
        {
            string query = http.Request.Url.RawWithQuery;
            int index = query.IndexOf('?');
            string url = _InternalUrl + (index >= 0 ? query.Substring(index) : String.Empty);
            HttpRequestMessage request = new HttpRequestMessage(new System.Net.Http.HttpMethod(verb), url);
            if (body != null && body.Length > 0) request.Content = new ByteArrayContent(body);

            NameValueCollection headers = http.Request.Headers;
            foreach (string? name in headers.AllKeys)
            {
                if (String.IsNullOrEmpty(name) || _HopHeaders.Contains(name) || _CredentialHeaders.Contains(name)) continue;
                if (name.StartsWith("x-hugo-mcp-proxy-", StringComparison.OrdinalIgnoreCase)) continue;
                string? value = headers[name];
                if (value == null) continue;
                if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.Content == null) request.Content = new ByteArrayContent(Array.Empty<byte>());
                    request.Content.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }
            request.Headers.TryAddWithoutValidation(ProxySecretHeader, _Secret);
            request.Headers.TryAddWithoutValidation(ProxyHandleHeader, handle);
            request.Headers.TryAddWithoutValidation(ProxyPrincipalHeader, caller.SessionPrincipal());
            if (!String.IsNullOrEmpty(http.Request.Source.IpAddress)) request.Headers.TryAddWithoutValidation(ProxyClientHeader, http.Request.Source.IpAddress);

            try
            {
                return await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, http.Token).ConfigureAwait(false);
            }
            finally
            {
                request.Dispose();
            }
        }

        private async Task<string> RelayAsync(HttpContextBase http, HttpResponseMessage response)
        {
            CancellationToken token = http.Token;
            http.Response.StatusCode = (int)response.StatusCode;
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
            {
                if (_HopHeaders.Contains(header.Key)) continue;
                if (header.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase) || String.Equals(header.Key, "Vary", StringComparison.OrdinalIgnoreCase)) continue;
                if (String.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    http.Response.ContentType = String.Join(", ", header.Value);
                    continue;
                }
                foreach (string value in header.Value) http.Response.Headers.Add(header.Key, value);
            }
            AddCors(http);

            // Only event streams are relayed chunk by chunk. Everything else, including bodyless 202 answers that the
            // internal listener sends chunked, is buffered and sent with a length: Watson's SendChunk writes the
            // terminating chunk twice when the final chunk is empty, and clients reject the trailing bytes (Node's HTTP
            // parser fails with "Expected HTTP/") or read them as the next response on a reused connection. Event
            // streams are marked Connection: close, so a client stops reading at the end of the stream (Garrison fix).
            bool streaming = String.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
            if (!streaming)
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                if (bytes.Length == 0) await http.Response.Send(token).ConfigureAwait(false);
                else await http.Response.Send(bytes, token).ConfigureAwait(false);
                return Outcome((int)response.StatusCode, bytes);
            }

            http.Response.Headers.Add("Connection", "close");
            http.Response.ChunkedTransfer = true;
            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            {
                byte[] buffer = new byte[16384];
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read <= 0) break;
                    byte[] chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                    await http.Response.SendChunk(chunk, false, token).ConfigureAwait(false);
                }
            }
            await http.Response.SendChunk(Array.Empty<byte>(), true, token).ConfigureAwait(false);
            return "stream";
        }

        private static string Outcome(int status, byte[] bytes)
        {
            if (status == 401) return "unauthenticated";
            if (status == 403) return "forbidden";
            if (status == 404) return "not_found";
            if (status >= 500) return "error";
            if (status >= 400) return "bad_request";
            if (bytes.Length == 0 || bytes[0] != (byte)'{') return "ok";
            try
            {
                McpResponseEnvelope? envelope = Serializer.Deserialize<McpResponseEnvelope>(Encoding.UTF8.GetString(bytes));
                if (envelope?.Error != null) return "error";
                if (envelope?.Result?.IsError == true) return "tool_error";
            }
            catch (Exception e) when (e is JsonException || e is NotSupportedException)
            {
                return "ok";
            }
            return "ok";
        }

        private void AddCors(HttpContextBase http)
        {
            string? origin = http.Request.Headers["Origin"];
            if (String.IsNullOrEmpty(origin)) return;
            List<string> allowed = _Context.Settings.Rest.CorsOrigins;
            string value = allowed.Count == 0 || allowed.Contains("*") ? "*" : (allowed.Contains(origin) ? origin : String.Empty);
            if (value.Length == 0) return;
            http.Response.Headers["Access-Control-Allow-Origin"] = value;
            http.Response.Headers["Access-Control-Expose-Headers"] = "Mcp-Session-Id, MCP-Protocol-Version, WWW-Authenticate, x-request-id, Retry-After";
        }

        private async Task SendErrorAsync(HttpContextBase http, int status, JsonElement? id, int code, string message)
        {
            McpErrorEnvelope error = new McpErrorEnvelope();
            error.Id = id;
            error.Error.Code = code;
            error.Error.Message = message;
            http.Response.StatusCode = status;
            http.Response.ContentType = "application/json";
            AddCors(http);
            await http.Response.Send(Serializer.Serialize(error), http.Token).ConfigureAwait(false);
        }

        private static McpRequestEnvelope? Parse(byte[]? body)
        {
            if (body == null || body.Length == 0) return null;
            int start = 0;
            while (start < body.Length && (body[start] == ' ' || body[start] == '\r' || body[start] == '\n' || body[start] == '\t')) start++;
            if (start >= body.Length || body[start] != (byte)'{') return null;
            try
            {
                return Serializer.Deserialize<McpRequestEnvelope>(Encoding.UTF8.GetString(body));
            }
            catch (Exception e) when (e is JsonException || e is NotSupportedException || e is ArgumentException)
            {
                return null;
            }
        }

        private static string MethodLabel(string verb, McpRequestEnvelope? envelope)
        {
            if (verb != "POST") return verb;
            if (envelope == null) return "other";
            if (envelope.Method == null) return "response";
            return _KnownMethods.Contains(envelope.Method) ? envelope.Method : "other";
        }

        private static string? Query(string rawWithQuery)
        {
            int index = rawWithQuery.IndexOf('?');
            return index >= 0 ? rawWithQuery.Substring(index + 1) : null;
        }

        #endregion
    }
}
