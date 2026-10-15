namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.CloudWatch.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_cloudwatch_get_metric_data</c> (Read): cloudwatch:GetMetricData for up to ten metrics or expressions over
    /// a bounded window. Returns a per-metric summary (points, min, average, max, latest) and the datapoints, capped by the
    /// output limits. Thread-safe.
    /// </summary>
    public class AwsCloudWatchGetMetricDataTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_cloudwatch_get_metric_data";

        /// <summary>
        /// Most datapoints per metric when the period is chosen automatically. Default 120, minimum 10, maximum 1440.
        /// </summary>
        public int TargetDatapoints
        {
            get
            {
                return _TargetDatapoints;
            }
            set
            {
                _TargetDatapoints = Math.Clamp(value, 10, 1440);
            }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _Id = new Regex("^[a-z][A-Za-z0-9_]{0,254}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private int _TargetDatapoints = 120;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsCloudWatchGetMetricDataTool() : base(BuildDescriptor(), AwsServiceNames.CloudWatch, "cloudwatch:GetMetricData")
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Choose a period, in seconds, that keeps a window to about <see cref="TargetDatapoints"/> points: a multiple of
        /// 60 seconds, at least 60.
        /// </summary>
        /// <param name="window">Window length.</param>
        /// <returns>Seconds.</returns>
        public int AutoPeriod(TimeSpan window)
        {
            double seconds = Math.Max(60, window.TotalSeconds / _TargetDatapoints);
            int minutes = (int)Math.Ceiling(seconds / 60.0);
            return Math.Max(60, minutes * 60);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            List<AwsMetricQuery> metrics = AwsArguments.Typed<List<AwsMetricQuery>>(arguments, "metrics") ?? new List<AwsMetricQuery>();
            if (metrics.Count == 0) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "metrics needs at least one entry with namespace and metricName, or an expression.");
            if (metrics.Count > 10) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "At most 10 metrics per call.");

            AwsTimeWindow window = AwsTimeWindow.Resolve(arguments, options, DateTime.UtcNow);
            int period = arguments.Has("period") ? arguments.GetInt("period", 60, 1, 86400 * 7) : AutoPeriod(window.Length);
            if (period >= 60 && period % 60 != 0) period = (period / 60 + 1) * 60;

            List<MetricDataQuery> queries = new List<MetricDataQuery>();
            Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> stats = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < metrics.Count; i++)
            {
                AwsMetricQuery m = metrics[i];
                string id = String.IsNullOrWhiteSpace(m.Id) ? "m" + i.ToString(CultureInfo.InvariantCulture) : m.Id.Trim();
                if (!_Id.IsMatch(id)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Metric id '" + id + "' must start with a lowercase letter and contain only letters, digits, and underscores.");
                MetricDataQuery q = new MetricDataQuery();
                q.Id = id;
                q.ReturnData = true;
                if (!String.IsNullOrWhiteSpace(m.Expression))
                {
                    q.Expression = m.Expression;
                    q.Period = period;
                    labels[id] = String.IsNullOrWhiteSpace(m.Label) ? m.Expression : m.Label;
                    stats[id] = "expression";
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(m.Namespace) || String.IsNullOrWhiteSpace(m.MetricName))
                        throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Metric " + (i + 1).ToString(CultureInfo.InvariantCulture) + " needs namespace and metricName (or an expression).");
                    string stat = String.IsNullOrWhiteSpace(m.Stat) ? "Average" : m.Stat.Trim();
                    Metric metric = new Metric();
                    metric.Namespace = m.Namespace;
                    metric.MetricName = m.MetricName;
                    metric.Dimensions = m.Dimensions.Select(kv => new Dimension { Name = kv.Key, Value = kv.Value }).ToList();
                    q.MetricStat = new MetricStat { Metric = metric, Period = period, Stat = stat };
                    labels[id] = String.IsNullOrWhiteSpace(m.Label) ? DescribeMetric(m) : m.Label;
                    stats[id] = stat;
                }
                if (!String.IsNullOrWhiteSpace(m.Label)) q.Label = m.Label;
                queries.Add(q);
            }

            GetMetricDataRequest request = new GetMetricDataRequest();
            request.MetricDataQueries = queries;
            request.StartTime = window.StartUtc;
            request.EndTime = window.EndUtc;
            request.ScanBy = Amazon.CloudWatch.ScanBy.TimestampAscending;

            Dictionary<string, List<KeyValuePair<DateTime, double>>> points = new Dictionary<string, List<KeyValuePair<DateTime, double>>>(StringComparer.Ordinal);
            List<string> messages = new List<string>();
            int pages = 0;
            do
            {
                token.ThrowIfCancellationRequested();
                GetMetricDataResponse response = await session.CloudWatch().GetMetricDataAsync(request, token).ConfigureAwait(false);
                foreach (MetricDataResult r in response.MetricDataResults ?? new List<MetricDataResult>())
                {
                    if (String.IsNullOrEmpty(r.Id)) continue;
                    if (!points.TryGetValue(r.Id, out List<KeyValuePair<DateTime, double>>? list))
                    {
                        list = new List<KeyValuePair<DateTime, double>>();
                        points[r.Id] = list;
                    }
                    List<DateTime> ts = r.Timestamps ?? new List<DateTime>();
                    List<double> vs = r.Values ?? new List<double>();
                    for (int i = 0; i < ts.Count && i < vs.Count; i++) list.Add(new KeyValuePair<DateTime, double>(ts[i], vs[i]));
                    if (r.StatusCode != null && r.StatusCode.Value != "Complete") messages.Add(r.Id + ": status " + r.StatusCode.Value);
                }
                foreach (MessageData md in response.Messages ?? new List<MessageData>()) messages.Add(md.Code + ": " + md.Value);
                request.NextToken = response.NextToken;
                pages++;
            }
            while (!String.IsNullOrEmpty(request.NextToken) && pages < 10);

            AwsTable table = new AwsTable("CloudWatch metrics " + window.ToString() + ", period " + period.ToString(CultureInfo.InvariantCulture) + "s", "id", "metric", "stat", "points", "min", "avg", "max", "latest", "latestAt");
            StringBuilder datapoints = new StringBuilder();
            int budget = context.Limits.MaxRows;
            bool cut = false;
            foreach (MetricDataQuery q in queries)
            {
                List<KeyValuePair<DateTime, double>> list = points.TryGetValue(q.Id, out List<KeyValuePair<DateTime, double>>? found) ? found.OrderBy(p => p.Key).ToList() : new List<KeyValuePair<DateTime, double>>();
                if (list.Count == 0)
                {
                    table.Add(q.Id, labels[q.Id], stats[q.Id], "0", null, null, null, null, null);
                    continue;
                }
                KeyValuePair<DateTime, double> last = list[list.Count - 1];
                table.Add(
                    q.Id,
                    labels[q.Id],
                    stats[q.Id],
                    list.Count.ToString(CultureInfo.InvariantCulture),
                    AwsResultBuilder.Number(list.Min(p => p.Value)),
                    AwsResultBuilder.Number(list.Average(p => p.Value)),
                    AwsResultBuilder.Number(list.Max(p => p.Value)),
                    AwsResultBuilder.Number(last.Value),
                    AwsResultBuilder.Time(last.Key));

                if (arguments.GetBool("includeDatapoints", true))
                {
                    IEnumerable<KeyValuePair<DateTime, double>> shown = list;
                    if (list.Count > budget)
                    {
                        shown = list.Skip(list.Count - Math.Max(0, budget));
                        cut = true;
                    }
                    List<KeyValuePair<DateTime, double>> shownList = shown.ToList();
                    budget -= shownList.Count;
                    if (shownList.Count > 0)
                    {
                        datapoints.Append(q.Id).Append(':');
                        foreach (KeyValuePair<DateTime, double> p in shownList)
                            datapoints.Append(' ').Append(AwsResultBuilder.Time(p.Key)).Append('=').Append(AwsResultBuilder.Number(p.Value));
                        datapoints.Append('\n');
                    }
                }
            }

            foreach (string message in messages.Distinct(StringComparer.Ordinal)) table.Notes.Add("CloudWatch: " + message);
            if (cut) table.Notes.Add("Datapoints were cut to the newest values within the output row limit; use a larger period or a shorter window.");
            if (datapoints.Length > 0) table.Trailer = "Datapoints (oldest first):\n" + datapoints.ToString();
            table.Truncated = cut;
            table.Details["startTime"] = AwsResultBuilder.Time(window.StartUtc);
            table.Details["endTime"] = AwsResultBuilder.Time(window.EndUtc);
            table.Details["period"] = period.ToString(CultureInfo.InvariantCulture);
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static string DescribeMetric(AwsMetricQuery m)
        {
            string text = m.Namespace + " " + m.MetricName;
            if (m.Dimensions.Count > 0) text += " (" + String.Join(", ", m.Dimensions.Select(kv => kv.Key + "=" + kv.Value)) + ")";
            return text;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Get CloudWatch metric data";
            d.Description =
                "Fetch CloudWatch metrics for an explicit time window: up to 10 metrics (namespace, metricName, dimensions, stat) "
                + "or metric math expressions referencing other ids. Returns min, average, max, and latest per metric plus the "
                + "datapoints. Use lookbackMinutes (default from the resource) or startTime and endTime in ISO 8601 UTC. The period "
                + "is chosen automatically unless given. Example metric: {\"namespace\":\"AWS/EC2\",\"metricName\":\"CPUUtilization\",\"dimensions\":{\"InstanceId\":\"i-0abc\"},\"stat\":\"Average\"}.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"metrics\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":10,\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"id\":{\"type\":\"string\",\"description\":\"Optional id (lowercase letter first), used by expressions.\"},"
                + "\"namespace\":{\"type\":\"string\"},\"metricName\":{\"type\":\"string\"},"
                + "\"dimensions\":{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"}},"
                + "\"stat\":{\"type\":\"string\",\"description\":\"Average, Sum, Minimum, Maximum, SampleCount, or pNN. Default Average.\"},"
                + "\"expression\":{\"type\":\"string\",\"description\":\"Metric math instead of a metric, for example m0 * 100.\"},"
                + "\"label\":{\"type\":\"string\"}}}},"
                + "\"lookbackMinutes\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Window length ending at endTime (default now).\"},"
                + "\"startTime\":{\"type\":\"string\",\"description\":\"ISO 8601 start (UTC when no offset).\"},"
                + "\"endTime\":{\"type\":\"string\",\"description\":\"ISO 8601 end. Default now.\"},"
                + "\"period\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Seconds per datapoint (1, 5, 10, 30, or a multiple of 60).\"},"
                + "\"includeDatapoints\":{\"type\":\"boolean\",\"description\":\"Include the raw datapoints. Default true.\"}"
                + "},\"required\":[\"metrics\"],\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A summary row per metric and the datapoints, bounded by the output limits.";
            return d;
        }

        #endregion
    }
}
