namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;

    /// <summary>
    /// In-process HTTP stub on 127.0.0.1 that speaks the AWS wire protocols the SDK uses for the stubbed services: awsQuery
    /// (form-encoded <c>Action=</c>, XML responses) for STS, EC2, RDS, ELBv2, and Auto Scaling, and awsJson 1.1
    /// (<c>X-Amz-Target</c>) for ECS. Point a resource's <c>endpointOverride</c> at <see cref="BaseAddress"/>. Responses
    /// come from <see cref="AwsRecordedResponses"/> and can be overridden or failed per action. Records each request's
    /// action, body, and Authorization header. Thread-safe.
    /// </summary>
    public class AwsStubServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base address, for example <c>http://127.0.0.1:50123</c>.
        /// </summary>
        public string BaseAddress { get; }

        /// <summary>
        /// Delay before each response, in milliseconds. Default 0.
        /// </summary>
        public int DelayMilliseconds { get; set; } = 0;

        /// <summary>
        /// Actions received, in order.
        /// </summary>
        public ConcurrentQueue<string> Actions { get; } = new ConcurrentQueue<string>();

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, AwsStubResponse> _Responses;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<AwsStubResponse>> _Failures = new ConcurrentDictionary<string, ConcurrentQueue<AwsStubResponse>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _LastBodies = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _LastAuthorization = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening on a free loopback port.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when no port could be bound.</exception>
        public AwsStubServer()
        {
            _Responses = new ConcurrentDictionary<string, AwsStubResponse>(AwsRecordedResponses.Defaults(), StringComparer.Ordinal);
            HttpListener? listener = null;
            string address = String.Empty;
            for (int attempt = 0; attempt < 10 && listener == null; attempt++)
            {
                address = "http://127.0.0.1:" + FreePort();
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
            if (listener == null) throw new InvalidOperationException("Could not bind the AWS stub listener on 127.0.0.1.");
            _Listener = listener;
            BaseAddress = address;
            Task.Run(() => AcceptLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the response of an action.
        /// </summary>
        /// <param name="action">Action, for example <c>DescribeDBInstances</c>.</param>
        /// <param name="response">Response.</param>
        public void Respond(string action, AwsStubResponse response)
        {
            _Responses[action] = response;
        }

        /// <summary>
        /// Answer the next calls of an action with a failure.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <param name="response">Failure response.</param>
        /// <param name="count">How many calls fail. Default 1.</param>
        public void FailNext(string action, AwsStubResponse response, int count = 1)
        {
            ConcurrentQueue<AwsStubResponse> queue = _Failures.GetOrAdd(action, _ => new ConcurrentQueue<AwsStubResponse>());
            for (int i = 0; i < count; i++) queue.Enqueue(response);
        }

        /// <summary>
        /// Body of the last request for an action, or an empty string.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>Body.</returns>
        public string LastBody(string action)
        {
            return _LastBodies.TryGetValue(action, out string? body) ? body : String.Empty;
        }

        /// <summary>
        /// Authorization header of the last request for an action, or an empty string.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>Header value.</returns>
        public string LastAuthorization(string action)
        {
            return _LastAuthorization.TryGetValue(action, out string? value) ? value : String.Empty;
        }

        /// <summary>
        /// Number of requests received for an action.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>Count.</returns>
        public int Count(string action)
        {
            int n = 0;
            foreach (string a in Actions) if (a == action) n++;
            return n;
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
                catch (Exception e) when (e is HttpListenerException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    return;
                }
                _ = Task.Run(() => HandleAsync(ctx, token));
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx, CancellationToken token)
        {
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                }

                string action = ActionOf(ctx.Request, body);
                Actions.Enqueue(action);
                _LastBodies[action] = body;
                _LastAuthorization[action] = ctx.Request.Headers["Authorization"] ?? String.Empty;

                if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, token).ConfigureAwait(false);

                AwsStubResponse? response = null;
                if (_Failures.TryGetValue(action, out ConcurrentQueue<AwsStubResponse>? failures) && failures.TryDequeue(out AwsStubResponse? failure)) response = failure;
                if (response == null && !_Responses.TryGetValue(action, out response))
                    response = AwsStubResponse.QueryError(400, "InvalidAction", "The stub has no recorded response for " + action + ".");

                byte[] bytes = Encoding.UTF8.GetBytes(response.Body);
                ctx.Response.StatusCode = response.Status;
                ctx.Response.ContentType = response.ContentType;
                ctx.Response.Headers["x-amzn-RequestId"] = "stub-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                foreach (KeyValuePair<string, string> header in response.Headers) ctx.Response.Headers[header.Key] = header.Value;
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                ctx.Response.Close();
            }
            catch (Exception e) when (e is OperationCanceledException || e is HttpListenerException || e is ObjectDisposedException || e is IOException)
            {
                try
                {
                    ctx.Response.Abort();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static string ActionOf(HttpListenerRequest request, string body)
        {
            string? target = request.Headers["X-Amz-Target"];
            if (!String.IsNullOrEmpty(target))
            {
                int dot = target.LastIndexOf('.');
                return dot >= 0 ? target.Substring(dot + 1) : target;
            }
            string? action = HttpUtility.ParseQueryString(body).Get("Action");
            if (!String.IsNullOrEmpty(action)) return action;
            return request.HttpMethod + " " + (request.Url?.AbsolutePath ?? "/");
        }

        #endregion
    }
}
