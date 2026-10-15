namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Responses;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Observability endpoints (Phase 13): the browser-facing URLs reported by server info, and reachability probes of
    /// Grafana, Prometheus, Loki, and Tempo run by the server against their internal URLs. Probe results are cached and
    /// shared by every caller; probes never throw. Thread-safe.
    /// </summary>
    public class ObservabilityService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Service names in the order they are reported.
        /// </summary>
        public static readonly IReadOnlyList<string> Services = new List<string> { "grafana", "prometheus", "loki", "tempo" };

        /// <summary>
        /// Seconds probe results are cached. Minimum 0 (no cache), maximum 3600, default 30.
        /// </summary>
        public int CacheSeconds
        {
            get
            {
                return _CacheSeconds;
            }
            set
            {
                _CacheSeconds = Math.Clamp(value, 0, 3600);
            }
        }

        /// <summary>
        /// Timeout for each probe request in milliseconds. Minimum 100, maximum 30000, default 2000.
        /// </summary>
        public int ProbeTimeoutMs
        {
            get
            {
                return _ProbeTimeoutMs;
            }
            set
            {
                _ProbeTimeoutMs = Math.Clamp(value, 100, 30000);
            }
        }

        /// <summary>
        /// Grafana datasource UIDs reported to clients. Defaults follow the Phase 13 contract.
        /// </summary>
        public ObservabilityDatasources Datasources
        {
            get
            {
                return _Datasources;
            }
            set
            {
                _Datasources = value ?? throw new ArgumentNullException(nameof(Datasources));
            }
        }

        /// <summary>
        /// Grafana dashboard UIDs reported to clients. Defaults follow the Phase 13 contract.
        /// </summary>
        public ObservabilityDashboards Dashboards
        {
            get
            {
                return _Dashboards;
            }
            set
            {
                _Dashboards = value ?? throw new ArgumentNullException(nameof(Dashboards));
            }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly ClusterSettingsService _Cluster;
        private readonly HttpClient _Http;
        private readonly SemaphoreSlim _ProbeLock = new SemaphoreSlim(1, 1);
        private int _CacheSeconds = 30;
        private int _ProbeTimeoutMs = 2000;
        private ObservabilityDatasources _Datasources = new ObservabilityDatasources();
        private ObservabilityDashboards _Dashboards = new ObservabilityDashboards();
        private List<ObservabilityServiceStatus>? _Cached = null;
        private string _CachedFingerprint = String.Empty;
        private DateTime _CachedUtc = DateTime.MinValue;
        private readonly GaugeRegistration _ReachableGauge;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="cluster">Cluster settings holding the Observability.* URLs.</param>
        /// <exception cref="ArgumentNullException">Thrown when cluster is null.</exception>
        public ObservabilityService(ClusterSettingsService cluster)
        {
            _Cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
            _Http = new HttpClient(new TelemetryHttpHandler("observability", new SocketsHttpHandler()));
            _Http.Timeout = Timeout.InfiniteTimeSpan;
            _ReachableGauge = HugoMetrics.RegisterGauge(HugoMetricCatalog.ObservabilityReachable, ObserveReachable);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Browser-facing URL of a service, or null when not configured.
        /// </summary>
        /// <param name="service">grafana, prometheus, loki, or tempo.</param>
        /// <returns>URL without a trailing slash, or null.</returns>
        public string? BrowserUrl(string service)
        {
            return Value("Observability." + SettingName(service) + "Url");
        }

        /// <summary>
        /// URL the server probes for a service: the internal URL when set, else the browser URL, else null.
        /// </summary>
        /// <param name="service">grafana, prometheus, loki, or tempo.</param>
        /// <returns>URL without a trailing slash, or null.</returns>
        public string? ProbeUrl(string service)
        {
            return Value("Observability." + SettingName(service) + "InternalUrl") ?? BrowserUrl(service);
        }

        /// <summary>
        /// Observability block for server info.
        /// </summary>
        /// <returns>Info; URLs are null when not configured, and UIDs are null when Grafana is not configured.</returns>
        public ObservabilityInfo Info()
        {
            ObservabilityInfo info = new ObservabilityInfo();
            info.GrafanaUrl = BrowserUrl("grafana");
            info.PrometheusUrl = BrowserUrl("prometheus");
            info.LokiUrl = BrowserUrl("loki");
            info.TempoUrl = BrowserUrl("tempo");
            if (info.GrafanaUrl != null)
            {
                info.Datasources = _Datasources;
                info.Dashboards = _Dashboards;
            }
            return info;
        }

        /// <summary>
        /// Reachability of every service, probing when the cache is older than <see cref="CacheSeconds"/> or the
        /// configured URLs changed. Never throws: failures are reported as unreachable.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One entry per service in <see cref="Services"/> order.</returns>
        public async Task<List<ObservabilityServiceStatus>> StatusAsync(CancellationToken token = default)
        {
            string fingerprint = Fingerprint();
            List<ObservabilityServiceStatus>? fresh = Fresh(fingerprint);
            if (fresh != null) return fresh;

            bool locked = false;
            try
            {
                await _ProbeLock.WaitAsync(token).ConfigureAwait(false);
                locked = true;
                fresh = Fresh(fingerprint);
                if (fresh != null) return fresh;

                List<Task<ObservabilityServiceStatus>> probes = Services.Select(s => ProbeAsync(s, token)).ToList();
                ObservabilityServiceStatus[] results = await Task.WhenAll(probes).ConfigureAwait(false);
                _Cached = results.ToList();
                _CachedFingerprint = fingerprint;
                _CachedUtc = DateTime.UtcNow;
                return Copy(_Cached);
            }
            catch (Exception)
            {
                return Services.Select(s => Unreachable(s, BrowserUrl(s) != null)).ToList();
            }
            finally
            {
                if (locked) _ProbeLock.Release();
            }
        }

        /// <summary>
        /// Drop cached probe results so the next status call probes again.
        /// </summary>
        public void Invalidate()
        {
            _CachedUtc = DateTime.MinValue;
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
                _Http.Dispose();
                _ReachableGauge.Dispose();
                _ProbeLock.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private static string SettingName(string service)
        {
            switch ((service ?? String.Empty).ToLowerInvariant())
            {
                case "grafana": return "Grafana";
                case "prometheus": return "Prometheus";
                case "loki": return "Loki";
                case "tempo": return "Tempo";
                default: throw new ArgumentException("Unknown observability service '" + service + "'.", nameof(service));
            }
        }

        private string? Value(string key)
        {
            string value = _Cluster.GetString(key);
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
        }

        private string Fingerprint()
        {
            return String.Join("|", Services.Select(s => (BrowserUrl(s) ?? String.Empty) + ">" + (ProbeUrl(s) ?? String.Empty)));
        }

        private List<ObservabilityServiceStatus>? Fresh(string fingerprint)
        {
            List<ObservabilityServiceStatus>? cached = _Cached;
            if (cached == null || _CacheSeconds == 0) return null;
            if (!String.Equals(fingerprint, _CachedFingerprint, StringComparison.Ordinal)) return null;
            if ((DateTime.UtcNow - _CachedUtc).TotalSeconds >= _CacheSeconds) return null;
            return Copy(cached);
        }

        private static List<ObservabilityServiceStatus> Copy(List<ObservabilityServiceStatus> source)
        {
            return source.Select(s => new ObservabilityServiceStatus
            {
                Service = s.Service,
                Configured = s.Configured,
                Reachable = s.Reachable,
                Version = s.Version,
                CheckedUtc = s.CheckedUtc
            }).ToList();
        }

        private static ObservabilityServiceStatus Unreachable(string service, bool configured)
        {
            ObservabilityServiceStatus status = new ObservabilityServiceStatus();
            status.Service = service;
            status.Configured = configured;
            status.Reachable = false;
            status.Version = null;
            status.CheckedUtc = DateTime.UtcNow;
            return status;
        }

        private async Task<ObservabilityServiceStatus> ProbeAsync(string service, CancellationToken token)
        {
            ObservabilityServiceStatus status = Unreachable(service, BrowserUrl(service) != null);
            string? baseUrl = ProbeUrl(service);
            if (!status.Configured || baseUrl == null) return status;

            try
            {
                switch (service)
                {
                    case "grafana":
                        string? health = await GetAsync(service, baseUrl + "/api/health", token).ConfigureAwait(false);
                        status.Reachable = health != null;
                        if (health != null) status.Version = Parse<BuildInfoVersion>(health)?.Version;
                        break;
                    case "prometheus":
                        status.Reachable = await GetAsync(service, baseUrl + "/-/ready", token).ConfigureAwait(false) != null;
                        if (status.Reachable)
                        {
                            string? build = await GetAsync(service, baseUrl + "/api/v1/status/buildinfo", token).ConfigureAwait(false);
                            if (build != null) status.Version = Parse<PrometheusBuildInfo>(build)?.Data?.Version;
                        }
                        break;
                    case "loki":
                        status.Reachable = await GetAsync(service, baseUrl + "/ready", token).ConfigureAwait(false) != null;
                        if (status.Reachable)
                        {
                            string? build = await GetAsync(service, baseUrl + "/loki/api/v1/status/buildinfo", token).ConfigureAwait(false);
                            if (build != null) status.Version = Parse<BuildInfoVersion>(build)?.Version;
                        }
                        break;
                    case "tempo":
                        status.Reachable = await GetAsync(service, baseUrl + "/ready", token).ConfigureAwait(false) != null;
                        if (status.Reachable)
                        {
                            string? build = await GetAsync(service, baseUrl + "/api/status/buildinfo", token).ConfigureAwait(false);
                            if (build != null) status.Version = Parse<BuildInfoVersion>(build)?.Version;
                        }
                        break;
                }
            }
            catch (Exception)
            {
                status.Reachable = false;
            }

            if (String.IsNullOrWhiteSpace(status.Version)) status.Version = null;
            status.CheckedUtc = DateTime.UtcNow;
            return status;
        }

        private IEnumerable<Measurement<double>> ObserveReachable()
        {
            List<ObservabilityServiceStatus>? cached = _Cached;
            if (cached == null) return Array.Empty<Measurement<double>>();
            return cached.Where(s => s.Configured).Select(s => HugoMetrics.Measure(s.Reachable ? 1 : 0, "service", s.Service)).ToList();
        }

        private async Task<string?> GetAsync(string service, string url, CancellationToken token)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(_ProbeTimeoutMs);
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Options.Set(TelemetryHttpHandler.ServiceKey, service);
                        request.Options.Set(TelemetryHttpHandler.OperationKey, "probe");
                        using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode) return null;
                            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is InvalidOperationException || e is UriFormatException)
                {
                    return null;
                }
            }
        }

        private static T? Parse<T>(string json) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, _JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
