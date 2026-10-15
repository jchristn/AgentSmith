namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal in-process HTTP server on 127.0.0.1 and a free port, built on <see cref="HttpListener"/>, for replaying
    /// recorded log service response shapes. Supports a response delay (timeouts, cancellation), forced 429 responses,
    /// and request recording. Thread-safe.
    /// </summary>
    public abstract class LoggingStubServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base address, for example <c>http://127.0.0.1:50123</c>.
        /// </summary>
        public string BaseAddress { get; }

        /// <summary>
        /// Token the stub accepts. Default <c>stub-token</c>.
        /// </summary>
        public string ApiToken { get; set; } = "stub-token";

        /// <summary>
        /// Delay before each response, in milliseconds. Default 0.
        /// </summary>
        public int DelayMilliseconds { get; set; } = 0;

        /// <summary>
        /// Seconds advertised in <c>Retry-After</c> on forced 429 responses. Default 7.
        /// </summary>
        public int RetryAfterSeconds { get; set; } = 7;

        /// <summary>
        /// Requests received, as path plus query string, in order.
        /// </summary>
        public ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, NameValueCollection> _LastQueries = new ConcurrentDictionary<string, NameValueCollection>(StringComparer.Ordinal);
        private int _RateLimitCount = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when no free port could be bound.</exception>
        protected LoggingStubServer()
        {
            HttpListener? listener = null;
            string address = String.Empty;
            for (int attempt = 0; attempt < 10 && listener == null; attempt++)
            {
                int port = FreePort();
                address = "http://127.0.0.1:" + port;
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
        /// Answer the next requests with 429.
        /// </summary>
        /// <param name="count">Number of requests.</param>
        public void FailNextWithRateLimit(int count = 1)
        {
            Interlocked.Exchange(ref _RateLimitCount, Math.Max(0, count));
        }

        /// <summary>
        /// Query parameters of the last request to a path.
        /// </summary>
        /// <param name="path">Path, for example <c>/v1/logs</c>.</param>
        /// <returns>Parameters, or an empty collection.</returns>
        public NameValueCollection LastQuery(string path)
        {
            return _LastQueries.TryGetValue(path, out NameValueCollection? q) ? q : new NameValueCollection();
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

        /// <summary>
        /// Whether the request carries valid credentials.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>True when authorized.</returns>
        protected abstract bool IsAuthorized(HttpListenerRequest request);

        /// <summary>
        /// Body of a 401 response.
        /// </summary>
        /// <returns>Object serialized as JSON.</returns>
        protected abstract object UnauthorizedBody();

        /// <summary>
        /// Headers added to every response (for example rate limit headers).
        /// </summary>
        /// <returns>Headers.</returns>
        protected virtual Dictionary<string, string> CommonHeaders()
        {
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// Handle an authorized request.
        /// </summary>
        /// <param name="path">Absolute path.</param>
        /// <param name="query">Query parameters.</param>
        /// <returns>Status code and body (null for an empty body).</returns>
        protected abstract StubResponse Handle(string path, NameValueCollection query);

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
                string path = ctx.Request.Url == null ? "/" : ctx.Request.Url.AbsolutePath;
                Requests.Enqueue(ctx.Request.Url == null ? path : ctx.Request.Url.PathAndQuery);
                NameValueCollection query = new NameValueCollection(ctx.Request.QueryString);
                _LastQueries[path] = query;

                if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, token).ConfigureAwait(false);

                Dictionary<string, string> headers = CommonHeaders();
                StubResponse response;
                if (!IsAuthorized(ctx.Request))
                {
                    response = new StubResponse(401, UnauthorizedBody());
                }
                else if (Interlocked.CompareExchange(ref _RateLimitCount, 0, 0) > 0 && Interlocked.Decrement(ref _RateLimitCount) >= 0)
                {
                    headers["Retry-After"] = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                    response = new StubResponse(429, null);
                }
                else
                {
                    response = Handle(path, query);
                }

                await WriteAsync(ctx, response, headers).ConfigureAwait(false);
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

        private static async Task WriteAsync(HttpListenerContext ctx, StubResponse response, Dictionary<string, string> headers)
        {
            ctx.Response.StatusCode = response.StatusCode;
            foreach (KeyValuePair<string, string> h in headers) ctx.Response.Headers[h.Key] = h.Value;
            byte[] bytes = response.Body == null
                ? Encoding.UTF8.GetBytes(response.StatusCode == 429 ? "Rate Limit Exceeded" : String.Empty)
                : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response.Body));
            ctx.Response.ContentType = response.Body == null ? "text/plain" : "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }

        #endregion
    }
}
