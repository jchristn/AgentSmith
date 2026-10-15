namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Settings;
    using Radiant;

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
            HugoMetrics.Labels.MaxLabelValues = settings.MaxLabelValues;
            if (!settings.Enabled) return;

            // Exemplars: the OpenTelemetry SDK attaches the current trace to histogram measurements when this filter is
            // trace_based, and the Prometheus exporter writes them in OpenMetrics scrapes. An explicit value wins.
            if (settings.Exemplars && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_METRICS_EXEMPLAR_FILTER")))
            {
                Environment.SetEnvironmentVariable("OTEL_METRICS_EXEMPLAR_FILTER", "trace_based");
            }

            Action<string>? counted = diagnostic == null ? null : new Action<string>(message =>
            {
                HugoMetrics.Count(HugoMetricCatalog.TelemetryDiagnostics, "severity", message.StartsWith("telemetry", StringComparison.Ordinal) ? "warning" : "info");
                diagnostic(message);
            });
            diagnostic = counted;

            List<string> hosts = PrometheusHosts(settings);
            for (int i = 0; i < hosts.Count; i++)
            {
                try
                {
                    _Host = RadiantHost.Start(Build(settings, nodeId, hosts[i], diagnostic));
                    _Client = _Host.Client;
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
                _Host?.Dispose();
                _Http.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

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
