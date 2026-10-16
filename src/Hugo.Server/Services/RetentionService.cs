namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Periodically prunes request history, audit records, and expired sessions according to cluster settings.
    /// Pruning is idempotent, so running it on every replica is safe until leader election gates it. Thread-safe.
    /// </summary>
    public class RetentionService
    {
        #region Public-Members

        /// <summary>
        /// Interval between passes in milliseconds. Minimum 60000, maximum 86400000, default 3600000.
        /// </summary>
        public int IntervalMs
        {
            get
            {
                return _IntervalMs;
            }
            set
            {
                _IntervalMs = Math.Clamp(value, 60000, 86400000);
            }
        }

        /// <summary>
        /// Optional predicate; when it returns false the pass is skipped (for example, not the leader).
        /// </summary>
        public Func<bool>? ShouldRun { get; set; } = null;

        /// <summary>
        /// Last time the retention loop completed an iteration or started (UTC).
        /// </summary>
        public DateTime LastHeartbeatUtc
        {
            get
            {
                return new DateTime(Interlocked.Read(ref _LastHeartbeatTicks), DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// Whether the loop has run within three intervals (background liveness for the health route).
        /// </summary>
        public bool Healthy
        {
            get
            {
                return DateTime.UtcNow - LastHeartbeatUtc < TimeSpan.FromMilliseconds(3.0 * _IntervalMs + 60000);
            }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly ClusterSettingsService _Cluster;
        private readonly HugoLog _Logging;
        private int _IntervalMs = 3600000;
        private long _LastHeartbeatTicks = DateTime.UtcNow.Ticks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="cluster">Cluster settings.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public RetentionService(DatabaseDriverBase database, ClusterSettingsService cluster, HugoLog logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one pruning pass.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Total rows pruned.</returns>
        public async Task<int> PruneOnceAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            int total = 0;
            total += Pruned("request_history", await _Database.RequestHistory.PruneAsync(now.AddDays(-_Cluster.GetInt("RequestHistory.RetentionDays")), token).ConfigureAwait(false));
            total += Pruned("audit", await _Database.Audit.PruneAsync(now.AddDays(-_Cluster.GetInt("Audit.RetentionDays")), token).ConfigureAwait(false));
            total += Pruned("auth_sessions", await _Database.Sessions.PruneExpiredAsync(now.AddDays(-1), token).ConfigureAwait(false));
            total += Pruned("nonces", await _Database.Nonces.PruneExpiredAsync(now, token).ConfigureAwait(false));
            total += Pruned("rate_limit_buckets", await _Database.RateLimits.PruneExpiredAsync(now, token).ConfigureAwait(false));
            return total;
        }

        /// <summary>
        /// Prune on an interval until cancelled.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_IntervalMs, token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _LastHeartbeatTicks, DateTime.UtcNow.Ticks);
                    if (ShouldRun != null && !ShouldRun()) continue;
                    int pruned = 0;
                    await HugoTracing.BackgroundAsync("retention", "prune", async ct =>
                    {
                        pruned = await PruneOnceAsync(ct).ConfigureAwait(false);
                        return pruned > 0;
                    }, token).ConfigureAwait(false);
                    if (pruned > 0) _Logging.Info("[Retention] pruned " + pruned + " rows");
                    Interlocked.Exchange(ref _LastHeartbeatTicks, DateTime.UtcNow.Ticks);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[Retention] pass failed: " + e.Message);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static int Pruned(string kind, int count)
        {
            if (count > 0) HugoMetrics.Count(HugoMetricCatalog.RetentionDeleted, count, HugoMetrics.Tags("kind", kind));
            return count;
        }

        #endregion
    }
}
