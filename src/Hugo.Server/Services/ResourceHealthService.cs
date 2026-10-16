namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Settings;

    /// <summary>
    /// Sequential, tenant-scoped resource health sweeps. Publishes bounded aggregate labels, never resource or tenant
    /// identifiers. Configuration changes and resource/auth invalidations drop observations until the next sweep.
    /// Thread-safe; overlapping sweeps serialize. Dispose after stopping the background loop.
    /// </summary>
    public class ResourceHealthService : IDisposable
    {
        private readonly DatabaseDriverBase _Database;
        private readonly ResourceService _Resources;
        private readonly AuthorizationService _Authorization;
        private readonly ConnectorsSettings _Settings;
        private readonly Action<string>? _Diagnostic;
        private readonly GaugeRegistration _Gauge;
        private readonly SemaphoreSlim _Sweep = new SemaphoreSlim(1, 1);
        private readonly object _Lock = new object();
        private List<Measurement<double>> _Measurements = new List<Measurement<double>>();
        private string _Configuration = String.Empty;
        private long _Generation;
        private bool _Disposed;

        /// <summary>
        /// Instantiate and register the observable gauge.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="resources">Resource service.</param>
        /// <param name="authorization">Identity invalidation source, including tenant deletion.</param>
        /// <param name="settings">Sweep settings, read each iteration.</param>
        /// <param name="diagnostic">Optional scrubbed failure callback.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ResourceHealthService(DatabaseDriverBase database, ResourceService resources, AuthorizationService authorization, ConnectorsSettings settings, Action<string>? diagnostic = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Diagnostic = diagnostic;
            _Resources.Changed += ResourceChanged;
            _Authorization.Invalidated += IdentityChanged;
            _Gauge = HugoMetrics.RegisterGauge(HugoMetricCatalog.ResourceHealth, Observe);
        }

        /// <summary>
        /// Probe eligible active resources in active tenants and replace the metric snapshot. Failures of individual
        /// probes do not stop later resources. Cancellation discards partial observations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of completed probes, including failed probes.</returns>
        public async Task<int> SweepAsync(CancellationToken token)
        {
            await _Sweep.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string configuration = Configuration();
                long generation;
                lock (_Lock)
                {
                    generation = _Generation;
                }
                if (_Settings.HealthIntervalSeconds <= 0) return 0;
                HashSet<string> allowed = Allowed();
                Dictionary<string, Dictionary<string, int>> counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
                int tested = 0;
                EnumerationQuery tenants = new EnumerationQuery { MaxResults = 1000, ActiveOnly = true };
                while (true)
                {
                    EnumerationResult<Tenant> page = await _Database.Tenants.EnumerateAsync(tenants, token).ConfigureAwait(false);
                    foreach (Tenant tenant in page.Objects)
                    {
                        EnumerationQuery query = new EnumerationQuery { MaxResults = 1000, ActiveOnly = true };
                        while (true)
                        {
                            EnumerationResult<Resource> resources = await _Database.Resources.EnumerateAsync(tenant.Id, query, null, token).ConfigureAwait(false);
                            foreach (Resource resource in resources.Objects)
                            {
                                token.ThrowIfCancellationRequested();
                                if (!allowed.Contains(resource.ConnectorType)) continue;
                                bool? success = null;
                                try
                                {
                                    Hugo.Core.Responses.ResourceTestResponse? result = await _Resources.TestScheduledAsync(tenant.Id, resource.Id, token).ConfigureAwait(false);
                                    success = result?.Success;
                                }
                                catch (Exception e) when (e is not OperationCanceledException)
                                {
                                    success = false;
                                    _Diagnostic?.Invoke(Hugo.Core.Security.SecretScrubber.Shared.Scrub(e.Message) ?? "Resource health probe failed.");
                                }
                                if (!success.HasValue) continue;
                                tested++;
                                if (!counts.TryGetValue(resource.ConnectorType, out Dictionary<string, int>? statuses))
                                {
                                    statuses = new Dictionary<string, int> { { "healthy", 0 }, { "unhealthy", 0 } };
                                    counts[resource.ConnectorType] = statuses;
                                }
                                statuses[success.Value ? "healthy" : "unhealthy"]++;
                            }
                            if (resources.EndOfResults || resources.Objects.Count == 0) break;
                            query.Skip += resources.Objects.Count;
                        }
                    }
                    if (page.EndOfResults || page.Objects.Count == 0) break;
                    tenants.Skip += page.Objects.Count;
                }
                token.ThrowIfCancellationRequested();
                List<Measurement<double>> measurements = new List<Measurement<double>>();
                foreach (KeyValuePair<string, Dictionary<string, int>> connector in counts)
                {
                    foreach (KeyValuePair<string, int> status in connector.Value)
                    {
                        measurements.Add(new Measurement<double>(status.Value, new KeyValuePair<string, object?>("connector", HugoMetrics.Labels.Fold("connector", connector.Key)), new KeyValuePair<string, object?>("status", status.Key)));
                    }
                }
                lock (_Lock)
                {
                    if (generation == _Generation && configuration == Configuration() && !_Disposed)
                    {
                        _Configuration = configuration;
                        _Measurements = measurements;
                    }
                }
                return tested;
            }
            finally
            {
                _Sweep.Release();
            }
        }

        /// <summary>
        /// Snapshot of aggregate measurements. Empty when disabled, invalidated, or awaiting the first sweep.
        /// </summary>
        /// <returns>Independent list of measurements.</returns>
        public IReadOnlyList<Measurement<double>> Observe()
        {
            lock (_Lock)
            {
                if (_Disposed || _Settings.HealthIntervalSeconds <= 0 || _Configuration != Configuration()) return Array.Empty<Measurement<double>>();
                return _Measurements.ToArray();
            }
        }

        /// <summary>
        /// Release the gauge callback and event subscriptions. Stop sweeps before disposal.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose implementation.
        /// </summary>
        /// <param name="disposing">Whether managed resources may be released.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            lock (_Lock)
            {
                if (_Disposed) return;
                _Disposed = true;
                _Measurements.Clear();
            }
            _Gauge.Dispose();
            _Resources.Changed -= ResourceChanged;
            _Authorization.Invalidated -= IdentityChanged;
            _Sweep.Dispose();
        }

        private HashSet<string> Allowed()
        {
            return new HashSet<string>(_Settings.HealthConnectorTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        }

        private string Configuration()
        {
            return _Settings.HealthIntervalSeconds + ":" + _Settings.HealthConnectorTypes;
        }

        private void ResourceChanged(object? sender, string tenantId)
        {
            Invalidate();
        }

        private void IdentityChanged(object? sender, EventArgs args)
        {
            Invalidate();
        }

        private void Invalidate()
        {
            lock (_Lock)
            {
                _Generation++;
                _Measurements.Clear();
            }
        }
    }
}
