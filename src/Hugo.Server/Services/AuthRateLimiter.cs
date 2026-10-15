namespace Hugo.Server.Services
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Fixed-window rate limiter for authentication routes, keyed by source IP. Counters live in the database
    /// (rate_limit_buckets), so the limit is the same whichever replica a request reaches and however many replicas run
    /// (P10.3). Each attempt costs one upsert; token requests are rare enough for that. When the database cannot be
    /// reached the attempt is allowed, because the login that follows needs the database anyway. Thread-safe.
    /// </summary>
    public class AuthRateLimiter
    {
        #region Public-Members

        /// <summary>
        /// Name of the limited operation, part of each bucket key. Default token.
        /// </summary>
        public string Scope
        {
            get
            {
                return _Scope;
            }
            set
            {
                _Scope = String.IsNullOrWhiteSpace(value) ? "token" : value;
            }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly Func<int> _LimitPerMinute;
        private string _Scope = "token";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database holding the shared counters.</param>
        /// <param name="limitPerMinute">Callback returning the current limit, so settings changes apply live.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AuthRateLimiter(DatabaseDriverBase database, Func<int> limitPerMinute)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _LimitPerMinute = limitPerMinute ?? throw new ArgumentNullException(nameof(limitPerMinute));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count an attempt. Returns false when the caller exceeded the limit for the current minute on any replica.
        /// </summary>
        /// <param name="key">Caller key, normally the source IP.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when allowed.</returns>
        public async Task<bool> TryAcquireAsync(string? key, CancellationToken token = default)
        {
            string caller = String.IsNullOrEmpty(key) ? "unknown" : key;
            DateTime now = DateTime.UtcNow;
            long minute = now.Ticks / TimeSpan.TicksPerMinute;
            DateTime windowEnd = new DateTime((minute + 1) * TimeSpan.TicksPerMinute, DateTimeKind.Utc);
            string bucket = _Scope + "|" + caller + "|" + minute.ToString(CultureInfo.InvariantCulture);
            try
            {
                int hits = await _Database.RateLimits.IncrementAsync(bucket, windowEnd.AddMinutes(1), token).ConfigureAwait(false);
                bool allowed = hits <= _LimitPerMinute();
                if (!allowed) HugoMetrics.Count(HugoMetricCatalog.AuthRateLimited, "scope", _Scope);
                return allowed;
            }
            catch (Exception e) when (e is DbException || e is InvalidOperationException || e is TimeoutException)
            {
                return true;
            }
        }

        #endregion
    }
}
