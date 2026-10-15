namespace Hugo.Server.Services
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Telemetry;
    using WatsonWebserver.Core;

    /// <summary>
    /// Writes a Server-Sent Events response (P6.14, D-022): named events, a <c>retry</c> hint on the first event,
    /// <c>heartbeat</c> events while the stream is idle, and a closing event. Writes are serialized, so events from
    /// parallel tool calls and the heartbeat never interleave. A client that goes away makes later writes no-ops.
    /// Thread-safe.
    /// </summary>
    public class SseWriter : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Seconds of idle time before a heartbeat event is sent. Default 15, minimum 1, maximum 300.
        /// </summary>
        public int HeartbeatSeconds
        {
            get
            {
                return _HeartbeatSeconds;
            }
            set
            {
                _HeartbeatSeconds = Math.Clamp(value, 1, 300);
            }
        }

        /// <summary>
        /// Reconnect delay hint sent with the first event, in milliseconds. Default 3000, minimum 0 (no hint), maximum
        /// 600000.
        /// </summary>
        public int RetryMs
        {
            get
            {
                return _RetryMs;
            }
            set
            {
                _RetryMs = Math.Clamp(value, 0, 600000);
            }
        }

        /// <summary>
        /// Whether the client went away (a write failed).
        /// </summary>
        public bool Disconnected
        {
            get
            {
                return _Disconnected;
            }
        }

        /// <summary>
        /// Events written so far (heartbeats included).
        /// </summary>
        public long EventsWritten
        {
            get
            {
                return Interlocked.Read(ref _EventsWritten);
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpContextBase _Http;
        private readonly string _Stream;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private int _HeartbeatSeconds = 15;
        private int _RetryMs = 3000;
        private bool _Started = false;
        private bool _Closed = false;
        private bool _Disconnected = false;
        private long _EventsWritten = 0;
        private long _LastWrite = Stopwatch.GetTimestamp();
        private Task? _Heartbeat = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="http">HTTP context to write to.</param>
        /// <param name="stream">Stream label for the SSE metrics (chat, a2a, openai).</param>
        /// <exception cref="ArgumentNullException">Thrown when http is null.</exception>
        public SseWriter(HttpContextBase http, string stream)
        {
            _Http = http ?? throw new ArgumentNullException(nameof(http));
            _Stream = String.IsNullOrEmpty(stream) ? "chat" : stream;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the response headers and start the heartbeat. Nothing is sent until the first event.
        /// </summary>
        /// <param name="heartbeats">Whether to send heartbeat events while idle.</param>
        public void Start(bool heartbeats = true)
        {
            if (_Started) return;
            _Started = true;
            _Http.Response.StatusCode = 200;
            _Http.Response.ContentType = "text/event-stream";
            _Http.Response.Headers["Cache-Control"] = "no-cache";
            _Http.Response.Headers["X-Accel-Buffering"] = "no";
            _Http.Response.ServerSentEvents = true;
            if (heartbeats) _Heartbeat = Task.Run(() => HeartbeatLoopAsync(_Stop.Token));
        }

        /// <summary>
        /// Send a named event.
        /// </summary>
        /// <param name="eventName">Event name, or null for an unnamed (message) event.</param>
        /// <param name="data">Data (one JSON document).</param>
        /// <param name="id">Event identifier, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when written; false after the client went away or the stream closed.</returns>
        public async Task<bool> SendAsync(string? eventName, string data, string? id, CancellationToken token)
        {
            return await WriteAsync(eventName, data, id, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send the final event and close the stream. Later sends are ignored.
        /// </summary>
        /// <param name="eventName">Closing event name, or null for an unnamed event.</param>
        /// <param name="data">Data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task CloseAsync(string? eventName, string data, CancellationToken token)
        {
            _Stop.Cancel();
            await WriteAsync(eventName, data, null, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stop the heartbeat and release resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Release resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (disposing)
            {
                try
                {
                    _Stop.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                try
                {
                    _Heartbeat?.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }
                _Stop.Dispose();
                _Gate.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        private async Task<bool> WriteAsync(string? eventName, string data, string? id, bool final, CancellationToken token)
        {
            if (!_Started) Start();
            await _Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_Closed || _Disconnected) return false;
                ServerSentEvent message = new ServerSentEvent();
                if (!String.IsNullOrEmpty(eventName)) message.Event = eventName;
                if (!String.IsNullOrEmpty(id)) message.Id = id;
                message.Data = data ?? "{}";
                if (Interlocked.Read(ref _EventsWritten) == 0 && _RetryMs > 0) message.Retry = _RetryMs.ToString(CultureInfo.InvariantCulture);
                try
                {
                    await _Http.Response.SendEvent(message, final, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is OperationCanceledException || e is System.Net.HttpListenerException || e is System.Net.Sockets.SocketException || e is InvalidOperationException)
                {
                    _Disconnected = true;
                    return false;
                }
                if (final) _Closed = true;
                Interlocked.Increment(ref _EventsWritten);
                Interlocked.Exchange(ref _LastWrite, Stopwatch.GetTimestamp());
                HugoMetrics.Count(HugoMetricCatalog.SseEvents, "stream", _Stream);
                return true;
            }
            finally
            {
                _Gate.Release();
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _LastWrite)).TotalSeconds < _HeartbeatSeconds) continue;
                bool written = await WriteAsync("heartbeat", "{\"utc\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}", null, false, CancellationToken.None).ConfigureAwait(false);
                if (!written) return;
            }
        }

        #endregion
    }
}
