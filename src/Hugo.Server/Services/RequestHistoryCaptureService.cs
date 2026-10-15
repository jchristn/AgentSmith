namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Settings;
    using SyslogLogging;
    using WatsonWebserver.Core;

    /// <summary>
    /// Captures every completed HTTP request into request history. Entries are built synchronously from the
    /// context, queued on a bounded channel (dropped when full, never blocking the response), and written by a
    /// background loop. Secret headers and bodies on secret-bearing routes are redacted. Response bodies of request
    /// history routes are not kept, because they embed earlier entries and would copy history into itself on every
    /// read. Thread-safe.
    /// </summary>
    public class RequestHistoryCaptureService : IRequestObserver, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Value written in place of redacted headers and bodies.
        /// </summary>
        public static readonly string Redacted = "[redacted]";

        /// <summary>
        /// Entries dropped because the queue was full.
        /// </summary>
        public long Dropped
        {
            get
            {
                return Interlocked.Read(ref _Dropped);
            }
        }

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _RedactedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization", "proxy-authorization", "cookie", "set-cookie", "x-token", "x-secret-key", "x-api-key", "x-signature", "x-password", "x-access-key", "x-nonce"
        };

        private static readonly string _HistoryPathPrefix = "/v1.0/api/request-history";
        private static readonly string[] _SecretPathFragments = new string[] { "/secrets", "/token", "/credentials", "/users/me/password", "/password", "/settings" };

        private readonly DatabaseDriverBase _Database;
        private readonly RequestHistorySettings _Settings;
        private readonly ClusterSettingsService _Cluster;
        private readonly LoggingModule _Logging;
        private readonly Channel<RequestHistoryEntry> _Queue;
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private Task? _Writer = null;
        private long _Dropped = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="settings">Request history settings (body limits, read live).</param>
        /// <param name="cluster">Cluster settings (RequestHistory.Enabled, read live).</param>
        /// <param name="logging">Logging.</param>
        /// <param name="capacity">Queue capacity. Minimum 100, maximum 1000000, default 10000.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public RequestHistoryCaptureService(DatabaseDriverBase database, RequestHistorySettings settings, ClusterSettingsService cluster, LoggingModule logging, int capacity = 10000)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            BoundedChannelOptions options = new BoundedChannelOptions(Math.Clamp(capacity, 100, 1000000));
            options.FullMode = BoundedChannelFullMode.DropWrite;
            options.SingleReader = true;
            _Queue = Channel.CreateBounded<RequestHistoryEntry>(options);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether capture is currently enabled.
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool Enabled()
        {
            return _Cluster.GetBool("RequestHistory.Enabled");
        }

        /// <summary>
        /// Start the background writer.
        /// </summary>
        public void Start()
        {
            if (_Writer != null) return;
            _Writer = Task.Run(() => WriteLoopAsync(_Stopping.Token));
        }

        /// <summary>
        /// Stop the writer after draining queued entries (bounded wait).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(CancellationToken token = default)
        {
            _Queue.Writer.TryComplete();
            if (_Writer == null) return;
            try
            {
                await _Writer.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _Stopping.Cancel();
            }
            catch (OperationCanceledException)
            {
                _Stopping.Cancel();
            }
        }

        /// <summary>
        /// Whether a finished request is captured: true while request history is enabled.
        /// </summary>
        /// <param name="path">Path without query.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="aborted">Whether the client went away.</param>
        /// <returns>True to capture.</returns>
        public bool ShouldCapture(string? path, int statusCode, bool aborted)
        {
            if (!Enabled()) return false;
            return true;
        }

        /// <inheritdoc />
        public void Observe(HttpContextBase context)
        {
            if (context == null || !ShouldCapture(context.Request.Url.RawWithoutQuery, context.Response.StatusCode, context.RequestAborted)) return;
            try
            {
                RequestHistoryEntry entry = Build(context);
                if (!_Queue.Writer.TryWrite(entry))
                {
                    Interlocked.Increment(ref _Dropped);
                    HugoMetrics.Count(HugoMetricCatalog.RequestHistoryCaptured, "result", "dropped");
                }
            }
            catch (Exception e)
            {
                _Logging.Debug("[RequestHistory] capture failed: " + e.Message);
            }
        }

        /// <summary>
        /// Build an entry from a completed request.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Entry.</returns>
        public RequestHistoryEntry Build(HttpContextBase context)
        {
            RequestHistoryEntry entry = new RequestHistoryEntry();
            entry.Method = context.Request.Method.ToString();
            entry.Path = Truncate(context.Request.Url.RawWithoutQuery ?? String.Empty, 1024);
            entry.Url = SecretScrubber.Shared.Scrub(context.Request.Url.RawWithQuery ?? entry.Path) ?? entry.Path;
            entry.StatusCode = context.Response.StatusCode;
            entry.DurationMs = Math.Round(context.Timestamp.TotalMs ?? 0, 3);
            entry.CreatedUtc = context.Timestamp.Start;
            entry.CompletedUtc = context.Timestamp.End ?? DateTime.UtcNow;
            entry.SourceIp = context.Request.Source.IpAddress;

            bool suppressBodies = false;
            if (context.Metadata is RequestContext ctx)
            {
                suppressBodies = ctx.SuppressHistoryBodies;
                entry.CorrelationId = ctx.RequestId;
                if (ctx.IsAuthenticated)
                {
                    entry.TenantId = ctx.TenantId;
                    entry.UserId = ctx.UserId;
                    entry.PrincipalName = ctx.PrincipalName;
                }
            }
            if (String.IsNullOrEmpty(entry.CorrelationId)) entry.CorrelationId = context.Request.Headers[Hugo.Core.Constants.HeaderRequestId];
            entry.TraceId = RequestTelemetry.TraceId(context);

            AddHeaders(entry, context.Request.Headers, HeaderDirectionEnum.Request);
            AddHeaders(entry, context.Response.Headers, HeaderDirectionEnum.Response);

            bool secretRoute = IsSecretRoute(entry.Path);
            bool historyRoute = IsHistoryRoute(entry.Path);
            string requestBody = SafeRequestBody(context);
            string responseBody = SafeResponseBody(context);
            long requestBytes = Encoding.UTF8.GetByteCount(requestBody);
            long responseBytes = Encoding.UTF8.GetByteCount(responseBody);

            entry.RequestBodyBytes = requestBytes;
            entry.ResponseBodyBytes = responseBytes;
            if (suppressBodies)
            {
                // Routes that return plaintext secrets (reveal) are recorded without any body (P2.7).
                entry.RequestBody = null;
                entry.ResponseBody = null;
            }
            else if (secretRoute)
            {
                entry.RequestBody = requestBytes > 0 ? Redacted : null;
                entry.ResponseBody = responseBytes > 0 ? Redacted : null;
            }
            else
            {
                entry.RequestBody = SecretScrubber.Shared.Scrub(Clip(requestBody, _Settings.MaxRequestBodyBytes, out bool requestTruncated));
                entry.RequestBodyTruncated = requestTruncated;
                if (historyRoute)
                {
                    // Request history responses embed earlier entries; keeping them would copy history into itself.
                    entry.ResponseBody = null;
                }
                else
                {
                    entry.ResponseBody = SecretScrubber.Shared.Scrub(Clip(responseBody, _Settings.MaxResponseBodyBytes, out bool responseTruncated));
                    entry.ResponseBodyTruncated = responseTruncated;
                }
            }
            return entry;
        }

        /// <summary>
        /// Whether a path carries secrets in its bodies.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>True when bodies must be redacted.</returns>
        public static bool IsSecretRoute(string? path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            foreach (string fragment in _SecretPathFragments)
            {
                if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a path belongs to the request history routes, whose response bodies are not captured.
        /// </summary>
        /// <param name="path">Path, without the query string. Null or empty is not a history route.</param>
        /// <returns>True when the response body must not be kept.</returns>
        public static bool IsHistoryRoute(string? path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            return path.StartsWith(_HistoryPathPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a header must be redacted.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>True when redacted.</returns>
        public static bool IsSecretHeader(string name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            if (_RedactedHeaders.Contains(name)) return true;
            string lower = name.ToLowerInvariant();
            return lower.Contains("token") || lower.Contains("secret") || lower.Contains("api-key") || lower.Contains("password");
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
            if (disposing)
            {
                _Stopping.Cancel();
                _Stopping.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task WriteLoopAsync(CancellationToken token)
        {
            try
            {
                while (await _Queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_Queue.Reader.TryRead(out RequestHistoryEntry? entry))
                    {
                        try
                        {
                            await _Database.RequestHistory.CreateAsync(entry, token).ConfigureAwait(false);
                            HugoMetrics.Count(HugoMetricCatalog.RequestHistoryCaptured, "result", "written");
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception e)
                        {
                            HugoMetrics.Count(HugoMetricCatalog.RequestHistoryCaptured, "result", "error");
                            _Logging.Warn("[RequestHistory] persist failed: " + e.Message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static void AddHeaders(RequestHistoryEntry entry, NameValueCollection? headers, HeaderDirectionEnum direction)
        {
            if (headers == null) return;
            foreach (string? key in headers.AllKeys)
            {
                if (String.IsNullOrEmpty(key)) continue;
                RequestHistoryHeader header = new RequestHistoryHeader();
                header.Direction = direction;
                header.Name = Truncate(key, 256);
                header.Value = IsSecretHeader(key) ? Redacted : (SecretScrubber.Shared.Scrub(headers[key]) ?? String.Empty);
                entry.Headers.Add(header);
            }
        }

        private static string? Clip(string body, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (String.IsNullOrEmpty(body)) return null;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            if (bytes.Length <= maxBytes) return body;
            truncated = true;
            if (maxBytes <= 0) return String.Empty;
            return Encoding.UTF8.GetString(bytes, 0, maxBytes);
        }

        private static string Truncate(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static string SafeRequestBody(HttpContextBase context)
        {
            try
            {
                return context.Request.DataAsString ?? String.Empty;
            }
            catch (Exception)
            {
                return String.Empty;
            }
        }

        private static string SafeResponseBody(HttpContextBase context)
        {
            try
            {
                return context.Response.DataAsString ?? String.Empty;
            }
            catch (Exception)
            {
                return String.Empty;
            }
        }

        #endregion
    }
}
