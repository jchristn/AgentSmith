namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Hugo.Core;
    using Hugo.Core.Settings;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// Application telemetry over Radiant: Prometheus listener, optional OTLP export, subscriptions to the Hugo meter
    /// and activity source (<see cref="HugoMetrics"/>, <see cref="HugoTracing"/>), Hugo.Worker, Watson,
    /// Voltaic, Padlock, and the Npgsql connection pool meter. Histogram buckets come from
    /// <see cref="HugoMetricCatalog"/>, and duration histograms carry trace exemplars when
    /// <see cref="TelemetrySettings.Exemplars"/> is on. Recording is best-effort and never throws. Thread-safe.
    /// </summary>
    public class HugoTelemetry : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Whether telemetry started successfully.
        /// </summary>
        public bool Active
        {
            get
            {
                return _Client != null;
            }
        }

        /// <summary>
        /// Host the Prometheus listener actually bound to, or null when telemetry or the listener is off.
        /// A configured wildcard (<c>*</c>, <c>+</c>, <c>0.0.0.0</c>) binds to the machine host name, which resolves to
        /// the pod IP in Kubernetes, and falls back to <c>127.0.0.1</c> if that fails.
        /// </summary>
        public string? PrometheusHostname { get; private set; } = null;

        /// <summary>
        /// URL of the Prometheus listener (http://host:port/path), or null when telemetry or the listener is off.
        /// </summary>
        public string? PrometheusUrl { get; private set; } = null;

        /// <summary>
        /// Timeout in milliseconds for <see cref="ScrapeAsync(CancellationToken)"/>. Minimum 100, maximum 60000,
        /// default 5000.
        /// </summary>
        public int ScrapeTimeoutMs
        {
            get
            {
                return _ScrapeTimeoutMs;
            }
            set
            {
                _ScrapeTimeoutMs = Math.Clamp(value, 100, 60000);
            }
        }

        #endregion

        #region Private-Members

        private readonly RadiantHost? _Host;
        private readonly RadiantClient? _Client;
        private readonly HttpClient _Http = new HttpClient();
        private readonly List<GaugeRegistration> _Gauges = new List<GaugeRegistration>();
        private int _ScrapeTimeoutMs = 5000;
        private bool _Disposed = false;
        private readonly CancellationTokenSource _LogStopping = new CancellationTokenSource();
        private readonly TelemetrySettings _Settings;
        private readonly string _Node;
        private Channel<HugoLogRecord>? _LogQueue;
        private Task? _LogPump;
        private ILogger? _Logger;
        private long _LogsDropped;

        /// <summary>Entries dropped because the bounded log bridge was full or shutting down.</summary>
        public long LogsDropped { get { return Interlocked.Read(ref _LogsDropped); } }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and start telemetry. Failures disable telemetry and are reported to the diagnostic callback.
        /// </summary>
        /// <param name="settings">Telemetry settings.</param>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="diagnostic">Diagnostic callback.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public HugoTelemetry(TelemetrySettings settings, string nodeId, Action<string>? diagnostic)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Settings = settings;
            _Node = nodeId ?? String.Empty;
            HugoMetrics.Labels.MaxLabelValues = settings.MaxLabelValues;
            if (!settings.Enabled) return;

            // Exemplars: the OpenTelemetry SDK attaches the current trace to histogram measurements when this filter is
            // trace_based, and the Prometheus exporter writes them in OpenMetrics scrapes. An explicit value wins.
            if (settings.Exemplars && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_METRICS_EXEMPLAR_FILTER")))
            {
                Environment.SetEnvironmentVariable("OTEL_METRICS_EXEMPLAR_FILTER", "trace_based");
            }

            // Capture the caller's callback in its own local: the lambda must not close over the parameter, because the
            // parameter is reassigned to the wrapper below and the wrapper would then call itself forever.
            Action<string>? original = diagnostic;
            Action<string>? counted = original == null ? null : new Action<string>(message =>
            {
                HugoMetrics.Count(HugoMetricCatalog.TelemetryDiagnostics, "severity", message.StartsWith("telemetry", StringComparison.Ordinal) ? "warning" : "info");
                try { original(message); }
                    catch (Exception) { }
            });
            diagnostic = counted;

            List<string> hosts = PrometheusHosts(settings);
            for (int i = 0; i < hosts.Count; i++)
            {
                try
                {
                    _Host = RadiantHost.Start(Build(settings, _Node, hosts[i], diagnostic));
                    _Client = _Host.Client;
                    if (settings.LogsEnable && (settings.OtlpEnable || settings.LokiEnable))
                    {
                        _Logger = _Host.CreateLogger("Hugo");
                        _LogQueue = Channel.CreateBounded<HugoLogRecord>(new BoundedChannelOptions(settings.LogQueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
                        _LogPump = Task.Run(() => LogLoopAsync(_LogStopping.Token));
                    }
                    PrometheusHostname = settings.PrometheusEnable ? hosts[i] : null;
                    PrometheusUrl = settings.PrometheusEnable ? "http://" + hosts[i] + ":" + settings.PrometheusPort + settings.PrometheusPath : null;
                    if (i > 0) diagnostic?.Invoke("telemetry: Prometheus listener bound to " + hosts[i] + " after " + hosts[i - 1] + " failed");
                    string process = settings.ServiceName.EndsWith("worker", StringComparison.OrdinalIgnoreCase) ? "worker" : "server";
                    _Gauges.Add(HugoMetrics.RegisterGauge(HugoMetricCatalog.BuildInfo, () => new Measurement<double>[]
                    {
                        new Measurement<double>(1, new KeyValuePair<string, object?>("version", Constants.ProductVersion), new KeyValuePair<string, object?>("process", process))
                    }));
                    return;
                }
                catch (Exception e)
                {
                    _Host = null;
                    _Client = null;
                    if (i < hosts.Count - 1) continue;
                    string detail = e.Message;
                    for (Exception? inner = e.InnerException; inner != null; inner = inner.InnerException) detail += " (" + inner.GetType().Name + ": " + inner.Message + ")";
                    diagnostic?.Invoke("telemetry disabled: " + detail);
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Queue an already scrubbed entry for Radiant. Never waits for network or queue capacity. Ambient scopes
        /// are excluded; trace context is captured now and restored only around this entry's export.
        /// </summary>
        /// <param name="entry">Scrubbed entry from HugoLog.</param>
        public void Log(LogEntry entry)
        {
            if (_Disposed || _LogQueue == null || entry == null) return;
            try
            {
                HugoLogRecord record = new HugoLogRecord();
                record.Message = Limit(entry.Message);
                record.Details = Limit(entry.ToJson());
                record.Level = entry.Severity.ToString();
                record.Severity = entry.Severity switch
                {
                    Severity.Debug => LogLevel.Debug,
                    Severity.Info => LogLevel.Information,
                    Severity.Warn => LogLevel.Warning,
                    Severity.Error => LogLevel.Error,
                    _ => LogLevel.Critical
                };
                record.TraceId = entry.TraceId ?? String.Empty;
                record.SpanId = entry.SpanId ?? String.Empty;
                record.Node = _Node;
                record.Activity = Activity.Current;
                if (!_LogQueue.Writer.TryWrite(record)) Interlocked.Increment(ref _LogsDropped);
            }
            catch (Exception) { Interlocked.Increment(ref _LogsDropped); }
        }

        /// <summary>
        /// Increment a counter.
        /// </summary>
        /// <param name="name">Metric name (hugo. prefix recommended).</param>
        /// <param name="tags">Low-cardinality tags.</param>
        public void Increment(string name, params RadiantTag[] tags)
        {
            if (_Client == null) return;
            try
            {
                _Client.Increment(name, 1.0, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Add an amount to a counter.
        /// </summary>
        /// <param name="name">Metric name.</param>
        /// <param name="amount">Non-negative amount.</param>
        /// <param name="tags">Low-cardinality tags.</param>
        public void Add(string name, double amount, params RadiantTag[] tags)
        {
            if (_Client == null) return;
            try
            {
                _Client.Increment(name, amount, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a histogram value.
        /// </summary>
        /// <param name="name">Metric name.</param>
        /// <param name="value">Value.</param>
        /// <param name="tags">Low-cardinality tags.</param>
        public void Record(string name, double value, params RadiantTag[] tags)
        {
            if (_Client == null) return;
            try
            {
                _Client.Record(name, value, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Register an observable gauge.
        /// </summary>
        /// <param name="name">Metric name.</param>
        /// <param name="observe">Value callback.</param>
        /// <param name="unit">Unit.</param>
        public void Gauge(string name, Func<double> observe, string unit)
        {
            if (_Client == null) return;
            try
            {
                _Client.RegisterGauge(name, observe, unit);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Register an observable gauge that reports one measurement per entity, each with its own tags (for example one
        /// value per status).
        /// </summary>
        /// <param name="name">Metric name.</param>
        /// <param name="observe">Callback returning the measurements; keep it fast and non-throwing.</param>
        /// <param name="unit">Unit.</param>
        public void Gauge(string name, Func<IEnumerable<Measurement<double>>> observe, string unit)
        {
            if (_Client == null) return;
            try
            {
                _Client.RegisterGauge(name, observe, unit);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Read the current Prometheus exposition from this process's own metrics listener, so another listener can
        /// serve it (P13.18). Never throws for listener failures.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exposition, or null when the listener is off or did not answer.</returns>
        public Task<MetricsScrape?> ScrapeAsync(CancellationToken token = default)
        {
            return ScrapeAsync(null, token);
        }

        /// <summary>
        /// Read the current exposition from this process's own metrics listener, passing the scraper's Accept header
        /// through so a Prometheus that asks for OpenMetrics gets exemplars. Never throws for listener failures.
        /// </summary>
        /// <param name="accept">Accept header of the original scrape, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exposition, or null when the listener is off or did not answer.</returns>
        public async Task<MetricsScrape?> ScrapeAsync(string? accept, CancellationToken token = default)
        {
            string? url = PrometheusUrl;
            if (url == null || _Disposed) return null;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(_ScrapeTimeoutMs);
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        if (!String.IsNullOrEmpty(accept)) request.Headers.TryAddWithoutValidation("Accept", accept);
                        using (HttpResponseMessage response = await _Http.SendAsync(request, timeout.Token).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode) return null;
                            MetricsScrape scrape = new MetricsScrape();
                            scrape.Body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                            string? contentType = response.Content.Headers.ContentType?.ToString();
                            if (!String.IsNullOrEmpty(contentType)) scrape.ContentType = contentType;
                            return scrape;
                        }
                    }
                }
                catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is InvalidOperationException || e is ObjectDisposedException)
                {
                    return null;
                }
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
            if (disposing)
            {
                foreach (GaugeRegistration gauge in _Gauges) gauge.Dispose();
                _Gauges.Clear();
                _LogQueue?.Writer.TryComplete();
                if (_LogPump != null)
                {
                    try
                    {
                        if (!_LogPump.Wait(_Settings.LogDrainTimeoutMs)) _LogStopping.Cancel();
                    }
                    catch (AggregateException) { }
                }
                try { _Host?.Dispose(); }
                catch (Exception) { }
                _LogStopping.Cancel();
                _LogStopping.Dispose();
                _Http.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private string Limit(string? text)
        {
            string safe = Hugo.Core.Security.SecretScrubber.Shared.Scrub(text) ?? String.Empty;
            return safe.Length <= _Settings.LogMaxCharacters ? safe : safe.Substring(0, _Settings.LogMaxCharacters) + " [truncated]";
        }

        private async Task LogLoopAsync(CancellationToken token)
        {
            try
            {
                while (await _LogQueue!.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_LogQueue.Reader.TryRead(out HugoLogRecord? record))
                    {
                        token.ThrowIfCancellationRequested();
                        Activity? previous = Activity.Current;
                        try
                        {
                            Activity.Current = record.Activity;
                            Dictionary<string, object> state = new Dictionary<string, object>
                            {
                                { "message", record.Message }, { "details", record.Details },
                                { "app", "hugo" }, { "component", "server" }, { "node", record.Node }, { "pipeline", "otlp" },
                                { "level", record.Level }, { "trace_id", record.TraceId }, { "span_id", record.SpanId }
                            };
                            _Logger!.Log(record.Severity, new EventId(0), state, null, (values, exception) => (string)values["message"]);
                        }
                        catch (Exception) { Interlocked.Increment(ref _LogsDropped); }
                        finally { Activity.Current = previous; }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        private static List<string> PrometheusHosts(TelemetrySettings settings)
        {
            List<string> hosts = new List<string>();
            string configured = settings.PrometheusHostname;
            if (configured == "*" || configured == "+" || configured == "0.0.0.0" || String.IsNullOrWhiteSpace(configured))
            {
                string machine = Dns.GetHostName();
                if (!String.IsNullOrEmpty(machine)) hosts.Add(machine);
                hosts.Add("127.0.0.1");
            }
            else
            {
                hosts.Add(configured);
            }
            return hosts;
        }

        private static RadiantSettings Build(TelemetrySettings settings, string nodeId, string host, Action<string>? diagnostic)
        {
            RadiantSettings radiant = new RadiantSettings();
            radiant.Enable = true;
            radiant.ServiceName = String.IsNullOrEmpty(settings.ServiceName) ? "hugo" : settings.ServiceName;
            radiant.ServiceInstanceId = String.IsNullOrEmpty(nodeId) ? null : nodeId;
            radiant.DiagnosticCallback = diagnostic;
            radiant.Metrics.IncludeRuntime = true;
            radiant.Metrics.IncludeProcess = true;
            radiant.Prometheus.Enable = settings.PrometheusEnable;
            radiant.Prometheus.Hostname = host;
            radiant.Prometheus.Port = settings.PrometheusPort;
            radiant.Prometheus.Path = settings.PrometheusPath;
            radiant.Logs.Enable = settings.LogsEnable;
            radiant.Logs.IncludeScopes = false;
            radiant.Logs.MinimumSeverity = 1;
            radiant.Loki.Enable = settings.LokiEnable;
            radiant.Loki.Endpoint = settings.LokiEndpoint;
            radiant.Loki.TenantId = String.IsNullOrEmpty(settings.LokiTenantId) ? null : settings.LokiTenantId;
            radiant.Otlp.TimeoutMs = settings.ExportTimeoutMs;
            radiant.Otlp.Enable = settings.OtlpEnable;
            if (!String.IsNullOrEmpty(settings.OtlpEndpoint)) radiant.Otlp.Endpoint = settings.OtlpEndpoint;
            radiant.Metrics.LabelPolicy = LabelPolicyEnum.Lenient;
            foreach (MetricDescriptor descriptor in HugoMetricCatalog.All())
            {
                if (descriptor.Kind != MetricKindEnum.Histogram || descriptor.Buckets == null) continue;
                MetricDefinition definition = new MetricDefinition(descriptor.Name, descriptor.Kind, descriptor.Unit, descriptor.Labels);
                definition.Buckets = descriptor.Buckets;
                definition.Description = descriptor.Description;
                radiant.Metrics.Define(definition);
            }
            radiant.Sources.AddMeter(HugoMetrics.MeterName);
            radiant.Sources.AddMeter("Npgsql");
            radiant.Sources.AddActivitySource("Hugo");
            radiant.Sources.AddActivitySource("Hugo.Worker");
            radiant.Sources.AddMeter("Watson");
            radiant.Sources.AddActivitySource("Watson");
            radiant.Sources.AddMeter("Voltaic");
            radiant.Sources.AddActivitySource("Voltaic");
            radiant.Sources.AddMeter("Padlock");
            radiant.Sources.AddActivitySource("Padlock");
            return radiant;
        }

        #endregion
    }
}
