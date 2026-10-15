namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Tenant analytics (/v1.0/api/analytics/{overview|tokens|tools|models}): turns, tokens, tool calls, approvals, and
    /// latency per time bucket for a window. Every report has the same shape; the kind decides which parts the
    /// dashboard emphasizes. The window defaults to the last 24 hours ending now; the interval defaults by window
    /// length (up to 6 hours: minute; up to 3 days: hour; longer: day).
    /// </summary>
    public class AnalyticsClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public AnalyticsClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Conversations, turns, tokens, tool calls, approvals, and latency per bucket.
        /// </summary>
        /// <param name="fromUtc">Window start, UTC, or null for 24 hours before the end.</param>
        /// <param name="toUtc">Window end, UTC, or null for now.</param>
        /// <param name="interval">minute, hour, day, or null to choose by window length.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status (400 ValidationFailed for a bad window or interval, or too many buckets).</exception>
        public Task<AnalyticsReport> GetOverviewAsync(DateTime? fromUtc = null, DateTime? toUtc = null, string? interval = null, CancellationToken token = default)
        {
            return ReportAsync("overview", fromUtc, toUtc, interval, token);
        }

        /// <summary>
        /// Token usage per bucket (input and output) with window totals.
        /// </summary>
        /// <param name="fromUtc">Window start, UTC, or null for 24 hours before the end.</param>
        /// <param name="toUtc">Window end, UTC, or null for now.</param>
        /// <param name="interval">minute, hour, day, or null to choose by window length.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<AnalyticsReport> GetTokensAsync(DateTime? fromUtc = null, DateTime? toUtc = null, string? interval = null, CancellationToken token = default)
        {
            return ReportAsync("tokens", fromUtc, toUtc, interval, token);
        }

        /// <summary>
        /// Tool calls, errors, and approvals per bucket, with per-tool rows.
        /// </summary>
        /// <param name="fromUtc">Window start, UTC, or null for 24 hours before the end.</param>
        /// <param name="toUtc">Window end, UTC, or null for now.</param>
        /// <param name="interval">minute, hour, day, or null to choose by window length.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<AnalyticsReport> GetToolsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, string? interval = null, CancellationToken token = default)
        {
            return ReportAsync("tools", fromUtc, toUtc, interval, token);
        }

        /// <summary>
        /// Turns, tokens, and latency per model endpoint, with per-bucket totals.
        /// </summary>
        /// <param name="fromUtc">Window start, UTC, or null for 24 hours before the end.</param>
        /// <param name="toUtc">Window end, UTC, or null for now.</param>
        /// <param name="interval">minute, hour, day, or null to choose by window length.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<AnalyticsReport> GetModelsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, string? interval = null, CancellationToken token = default)
        {
            return ReportAsync("models", fromUtc, toUtc, interval, token);
        }

        #endregion

        #region Private-Methods

        private Task<AnalyticsReport> ReportAsync(string kind, DateTime? fromUtc, DateTime? toUtc, string? interval, CancellationToken token)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?>
            {
                { "from", QueryBuilder.Date(fromUtc) },
                { "to", QueryBuilder.Date(toUtc) },
                { "interval", interval }
            };
            return GetAsync<AnalyticsReport>("/v1.0/api/analytics/" + kind, query, token);
        }

        #endregion
    }
}
