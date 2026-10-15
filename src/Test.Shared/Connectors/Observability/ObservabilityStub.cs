namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-process HTTP server on 127.0.0.1 and a free port, built on <see cref="HttpListener"/>, that replays recorded
    /// Prometheus, Loki, and OpenSearch response shapes through a handler. It can require a fixed header (Basic,
    /// Bearer, API key), act as an OAuth 2.0 token endpoint at <c>/oauth/token</c>, delay responses, force 429s, and
    /// records every request. Thread-safe.
    /// </summary>
    public class ObservabilityStub : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base address, for example <c>http://127.0.0.1:50123</c>.
        /// </summary>
        public string BaseAddress { get; }

        /// <summary>
        /// Route handler. Default returns 404.
        /// </summary>
        public Func<StubRequest, StubReply> Handler { get; set; } = r => StubReply.Status(404, "{\"error\":\"no route\"}");

        /// <summary>
        /// When set, API requests must carry this header name and value, or get 401.
        /// </summary>
        public string? RequiredHeaderName { get; set; } = null;

        /// <summary>
        /// Value of <see cref="RequiredHeaderName"/>.
        /// </summary>
        public string? RequiredHeaderValue { get; set; } = null;

        /// <summary>
        /// When set, the stub issues OAuth 2.0 tokens to this client ID (with <see cref="OAuthClientSecret"/>) and API
        /// requests need a valid issued bearer token.
        /// </summary>
        public string? OAuthClientId { get; set; } = null;

        /// <summary>
        /// Client secret expected at the token endpoint.
        /// </summary>
        public string? OAuthClientSecret { get; set; } = null;

        /// <summary>
        /// expires_in reported for issued tokens. Default 3600.
        /// </summary>
        public int OAuthExpiresIn { get; set; } = 3600;

        /// <summary>
        /// Token requests received.
        /// </summary>
        public int TokenRequests
        {
            get
            {
                return _TokenRequests;
            }
        }

        /// <summary>
        /// Scope parameter of the last token request.
        /// </summary>
        public string? LastScope { get; private set; } = null;

        /// <summary>
        /// Delay before each API response, in milliseconds. Default 0.
        /// </summary>
        public int DelayMilliseconds { get; set; } = 0;

        /// <summary>
        /// Requests received (API and token), in order.
        /// </summary>
        public ConcurrentQueue<StubRequest> Requests { get; } = new ConcurrentQueue<StubRequest>();

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, bool> _Tokens = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private int _TokenRequests = 0;
        private int _RateLimitCount = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when no free port could be bound.</exception>
        public ObservabilityStub()
        {
            HttpListener? listener = null;
            string address = String.Empty;
            for (int attempt = 0; attempt < 10 && listener == null; attempt++)
            {
                int port = FreePort();
                address = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
                HttpListener candidate = new HttpListener();
                candidate.Prefixes.Add(address + "/");
                try
                {
                    candidate.Start();
                    listener = candidate;
                }
                catch (HttpListenerException)
                {
                    candidate.Close();
                }
            }
            if (listener == null) throw new InvalidOperationException("Could not bind a stub HTTP listener on 127.0.0.1.");
            _Listener = listener;
            BaseAddress = address;
            Task.Run(() => AcceptLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Invalidate every issued OAuth token, so the next API call gets 401.
        /// </summary>
        public void RevokeTokens()
        {
            _Tokens.Clear();
        }

        /// <summary>
        /// Answer the next API requests with 429 and Retry-After 9.
        /// </summary>
        /// <param name="count">Number of requests.</param>
        public void FailNextWithRateLimit(int count = 1)
        {
            Interlocked.Exchange(ref _RateLimitCount, Math.Max(0, count));
        }

        /// <summary>
        /// The last API request to a path, or null.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Request.</returns>
        public StubRequest? Last(string path)
        {
            return Requests.Where(r => r.Path == path).LastOrDefault();
        }

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
        /// Stop listening.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
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
                HttpListenerContext ctx;
                try
                {
                    ctx = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                _ = Task.Run(() => ProcessAsync(ctx, token));
            }
        }

        private async Task ProcessAsync(HttpListenerContext ctx, CancellationToken token)
        {
            try
            {
                StubRequest request = new StubRequest
                {
                    Method = ctx.Request.HttpMethod,
                    Path = ctx.Request.Url == null ? "/" : Uri.UnescapeDataString(ctx.Request.Url.AbsolutePath),
                    RawUrl = ctx.Request.RawUrl ?? "/",
                    Query = new NameValueCollection(ctx.Request.QueryString),
                    Headers = new NameValueCollection(ctx.Request.Headers)
                };
                using (StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                {
                    request.Body = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                }
                Requests.Enqueue(request);

                StubReply reply;
                if (request.Path == "/oauth/token")
                {
                    reply = Token(request);
                }
                else
                {
                    if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, token).ConfigureAwait(false);
                    if (!Authorized(request))
                    {
                        reply = StubReply.Status(401, "{\"status\":\"error\",\"errorType\":\"unauthorized\",\"error\":\"authentication required\"}");
                    }
                    else if (Interlocked.CompareExchange(ref _RateLimitCount, 0, 0) > 0 && Interlocked.Decrement(ref _RateLimitCount) >= 0)
                    {
                        reply = StubReply.Status(429, "{\"error\":\"too many requests\"}");
                        reply.Headers["Retry-After"] = "9";
                    }
                    else
                    {
                        reply = Handler(request);
                    }
                }

                ctx.Response.StatusCode = reply.StatusCode;
                foreach (KeyValuePair<string, string> h in reply.Headers) ctx.Response.Headers[h.Key] = h.Value;
                byte[] bytes = Encoding.UTF8.GetBytes(reply.Body ?? String.Empty);
                ctx.Response.ContentType = reply.ContentType;
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                ctx.Response.Close();
            }
            catch (Exception)
            {
                try
                {
                    ctx.Response.Abort();
                }
                catch (Exception)
                {
                }
            }
        }

        private bool Authorized(StubRequest request)
        {
            if (OAuthClientId != null)
            {
                string? auth = request.Headers["Authorization"];
                if (auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
                return _Tokens.ContainsKey(auth.Substring(7));
            }
            if (RequiredHeaderName != null) return request.Headers[RequiredHeaderName] == RequiredHeaderValue;
            return true;
        }

        private StubReply Token(StubRequest request)
        {
            Interlocked.Increment(ref _TokenRequests);
            string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(OAuthClientId ?? String.Empty) + ":" + Uri.EscapeDataString(OAuthClientSecret ?? String.Empty)));
            NameValueCollection form = System.Web.HttpUtility.ParseQueryString(request.Body);
            LastScope = form["scope"];
            if (OAuthClientId == null || request.Headers["Authorization"] != expected || form["grant_type"] != "client_credentials")
                return StubReply.Status(401, "{\"error\":\"invalid_client\"}");
            string token = "stub-access-" + Guid.NewGuid().ToString("N");
            _Tokens[token] = true;
            return StubReply.Json("{\"access_token\":\"" + token + "\",\"token_type\":\"Bearer\",\"expires_in\":" + OAuthExpiresIn.ToString(CultureInfo.InvariantCulture) + "}");
        }

        #endregion
    }
}
