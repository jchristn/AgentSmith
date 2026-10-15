namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Services;

    /// <summary>
    /// Tenant analytics (P6.17, P6.18): reads token, turn, and tool rows for a window and aggregates them into time
    /// buckets in process, filling empty buckets with zeros so charts never have gaps. Thread-safe.
    /// </summary>
    public class AnalyticsService
    {
        #region Public-Members

        /// <summary>
        /// Most buckets one report may contain. Default 1000, minimum 10, maximum 10000.
        /// </summary>
        public int MaxBuckets
        {
            get
            {
                return _MaxBuckets;
            }
            set
            {
                _MaxBuckets = Math.Clamp(value, 10, 10000);
            }
        }

        /// <summary>
        /// Window used when the caller gives no start, in hours. Default 168 (seven days), minimum 1, maximum 8760.
        /// </summary>
        public int DefaultWindowHours
        {
            get
            {
                return _DefaultWindowHours;
            }
            set
            {
                _DefaultWindowHours = Math.Clamp(value, 1, 8760);
            }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private int _MaxBuckets = 1000;
        private int _DefaultWindowHours = 168;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public AnalyticsService(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a report.
        /// </summary>
        /// <param name="kind">overview, tokens, tools, or models.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="fromUtc">Window start, or null for <see cref="DefaultWindowHours"/> before the end.</param>
        /// <param name="toUtc">Window end, or null for now.</param>
        /// <param name="interval">minute, hour, day, or null to choose by window length (up to 6 hours: minute; up to 3 days: hour; longer: day).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        /// <exception cref="ValidationException">Thrown when the window or interval is invalid or needs too many buckets.</exception>
        public async Task<AnalyticsReport> BuildAsync(string kind, string tenantId, DateTime? fromUtc, DateTime? toUtc, string? interval, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            DateTime to = (toUtc ?? DateTime.UtcNow).ToUniversalTime();
            DateTime from = (fromUtc ?? to.AddHours(-_DefaultWindowHours)).ToUniversalTime();
            if (from >= to) throw new ValidationException("from must be earlier than to.", new Dictionary<string, string> { { "from", "Must be earlier than to." } });
            string unit = String.IsNullOrWhiteSpace(interval) ? Choose(to - from) : interval.Trim().ToLowerInvariant();
            TimeSpan step = Step(unit);
            DateTime start = Floor(from, unit);
            long count = (long)Math.Ceiling((to - start).TotalMilliseconds / step.TotalMilliseconds);
            if (count > _MaxBuckets) throw new ValidationException("The window needs " + count + " " + unit + " buckets; the limit is " + _MaxBuckets + ". Use a shorter window or a longer interval.", new Dictionary<string, string> { { "interval", "Too many buckets." } });

            AnalyticsReport report = new AnalyticsReport();
            report.Kind = kind;
            report.FromUtc = start;
            report.ToUtc = to;
            report.Interval = unit;
            for (int i = 0; i < count; i++)
            {
                AnalyticsBucket bucket = new AnalyticsBucket();
                bucket.StartUtc = start.AddTicks(step.Ticks * i);
                report.Buckets.Add(bucket);
            }

            List<MessageUsageRow> messages = await _Database.Analytics.ListMessageUsageAsync(tenantId, start, to, token).ConfigureAwait(false);
            List<ToolUsageRow> tools = await _Database.Analytics.ListToolUsageAsync(tenantId, start, to, token).ConfigureAwait(false);
            report.Conversations = await _Database.Analytics.CountConversationsAsync(tenantId, start, to, token).ConfigureAwait(false);
            report.ApprovalsRequested = await _Database.Analytics.CountApprovalsAsync(tenantId, start, to, token).ConfigureAwait(false);

            foreach (MessageUsageRow row in messages) AddMessage(Bucket(report, start, step, row.CreatedUtc), row);
            foreach (ToolUsageRow row in tools) AddTool(Bucket(report, start, step, row.CreatedUtc), row);
            foreach (AnalyticsBucket bucket in report.Buckets) Average(bucket, messages.Where(m => m.FinishReason != null && m.CreatedUtc >= bucket.StartUtc && m.CreatedUtc < bucket.StartUtc + step).ToList());

            AnalyticsBucket totals = new AnalyticsBucket();
            totals.StartUtc = start;
            foreach (MessageUsageRow row in messages) AddMessage(totals, row);
            foreach (ToolUsageRow row in tools) AddTool(totals, row);
            Average(totals, messages.Where(m => m.FinishReason != null).ToList());
            report.Totals = totals;

            if (String.Equals(kind, "tools", StringComparison.Ordinal)) report.Tools = ToolRows(tools);
            if (String.Equals(kind, "models", StringComparison.Ordinal)) report.Models = await ModelRowsAsync(tenantId, messages, token).ConfigureAwait(false);
            return report;
        }

        #endregion

        #region Private-Methods

        private static string Choose(TimeSpan span)
        {
            if (span <= TimeSpan.FromHours(6)) return "minute";
            if (span <= TimeSpan.FromDays(3)) return "hour";
            return "day";
        }

        private static TimeSpan Step(string unit)
        {
            switch (unit)
            {
                case "minute": return TimeSpan.FromMinutes(1);
                case "hour": return TimeSpan.FromHours(1);
                case "day": return TimeSpan.FromDays(1);
                default: throw new ValidationException("interval must be minute, hour, or day.", new Dictionary<string, string> { { "interval", "Must be minute, hour, or day." } });
            }
        }

        private static DateTime Floor(DateTime value, string unit)
        {
            switch (unit)
            {
                case "minute": return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
                case "hour": return new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);
                default: return new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc);
            }
        }

        private static AnalyticsBucket? Bucket(AnalyticsReport report, DateTime start, TimeSpan step, DateTime at)
        {
            long index = (at - start).Ticks / step.Ticks;
            if (index < 0 || index >= report.Buckets.Count) return null;
            return report.Buckets[(int)index];
        }

        private static void AddMessage(AnalyticsBucket? bucket, MessageUsageRow row)
        {
            if (bucket == null) return;
            bucket.InputTokens += row.InputTokens;
            bucket.OutputTokens += row.OutputTokens;
            if (row.FinishReason != null) bucket.Turns++;
        }

        private static void AddTool(AnalyticsBucket? bucket, ToolUsageRow row)
        {
            if (bucket == null) return;
            bucket.ToolCalls++;
            if (IsError(row) || IsDenied(row)) bucket.ToolErrors++;
            if (row.HadApproval) bucket.Approvals++;
        }

        private static void Average(AnalyticsBucket bucket, List<MessageUsageRow> turns)
        {
            List<long> ttft = turns.Where(t => t.TimeToFirstTokenMs.HasValue).Select(t => t.TimeToFirstTokenMs!.Value).ToList();
            List<long> duration = turns.Where(t => t.DurationMs.HasValue).Select(t => t.DurationMs!.Value).ToList();
            bucket.AvgTimeToFirstTokenMs = ttft.Count > 0 ? ttft.Average() : null;
            bucket.AvgTurnDurationMs = duration.Count > 0 ? duration.Average() : null;
        }

        private static bool IsError(ToolUsageRow row)
        {
            return row.Status == InvocationStatusEnum.Failed || row.Status == InvocationStatusEnum.TimedOut;
        }

        private static bool IsDenied(ToolUsageRow row)
        {
            return row.Decision == InvocationDecisionEnum.Denied || row.Decision == InvocationDecisionEnum.Rejected || row.Decision == InvocationDecisionEnum.Expired;
        }

        private static List<AnalyticsToolRow> ToolRows(List<ToolUsageRow> tools)
        {
            List<AnalyticsToolRow> rows = new List<AnalyticsToolRow>();
            foreach (IGrouping<string, ToolUsageRow> group in tools.GroupBy(t => t.ToolName, StringComparer.Ordinal))
            {
                AnalyticsToolRow row = new AnalyticsToolRow();
                row.ToolName = group.Key;
                row.ConnectorType = group.Select(t => t.ConnectorType).FirstOrDefault(c => !String.IsNullOrEmpty(c));
                row.Calls = group.LongCount();
                row.Errors = group.LongCount(IsError);
                row.Denied = group.LongCount(IsDenied);
                row.Approvals = group.LongCount(t => t.HadApproval);
                List<ToolUsageRow> ran = group.Where(t => t.Status == InvocationStatusEnum.Succeeded || IsError(t)).ToList();
                row.AvgDurationMs = ran.Count > 0 ? ran.Average(t => (double)t.DurationMs) : null;
                rows.Add(row);
            }
            return rows.OrderByDescending(r => r.Calls).ThenBy(r => r.ToolName, StringComparer.Ordinal).ToList();
        }

        private async Task<List<AnalyticsModelRow>> ModelRowsAsync(string tenantId, List<MessageUsageRow> messages, CancellationToken token)
        {
            List<AnalyticsModelRow> rows = new List<AnalyticsModelRow>();
            foreach (IGrouping<string, MessageUsageRow> group in messages.GroupBy(m => m.ModelEndpointId ?? String.Empty, StringComparer.Ordinal))
            {
                AnalyticsModelRow row = new AnalyticsModelRow();
                row.ModelEndpointId = group.Key.Length == 0 ? null : group.Key;
                if (row.ModelEndpointId != null)
                {
                    ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(tenantId, row.ModelEndpointId, token).ConfigureAwait(false);
                    if (endpoint != null)
                    {
                        row.Name = endpoint.Name;
                        row.Provider = endpoint.Provider.ToString();
                        row.Model = endpoint.Model;
                    }
                }
                List<MessageUsageRow> turns = group.Where(m => m.FinishReason != null).ToList();
                row.Turns = turns.Count;
                row.InputTokens = group.Sum(m => m.InputTokens);
                row.OutputTokens = group.Sum(m => m.OutputTokens);
                List<long> ttft = turns.Where(t => t.TimeToFirstTokenMs.HasValue).Select(t => t.TimeToFirstTokenMs!.Value).ToList();
                List<long> duration = turns.Where(t => t.DurationMs.HasValue).Select(t => t.DurationMs!.Value).ToList();
                row.AvgTimeToFirstTokenMs = ttft.Count > 0 ? ttft.Average() : null;
                row.AvgTurnDurationMs = duration.Count > 0 ? duration.Average() : null;
                rows.Add(row);
            }
            return rows.OrderByDescending(r => r.InputTokens + r.OutputTokens).ToList();
        }

        #endregion
    }
}
