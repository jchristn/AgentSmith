namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>swo_query_metric</c>: measurements of one SolarWinds Observability metric over a bounded window, summarized
    /// per grouping (min, max, average, last) with the data points. Read. Thread-safe.
    /// </summary>
    public class SwoQueryMetricTool : LoggingToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"name\"],\"properties\":{"
            + "\"name\":{\"type\":\"string\",\"description\":\"Metric name, for example system.cpu.utilization.\"},"
            + "\"entityId\":{\"type\":\"string\",\"description\":\"Restrict to one entity. Ignored when filter is set.\"},"
            + "\"filter\":{\"type\":\"string\",\"description\":\"Attribute filter in SolarWinds syntax, for example id:[e-123,e-456].\"},"
            + "\"groupBy\":{\"type\":\"string\",\"description\":\"Comma-separated attributes to group by, for example id.\"},"
            + "\"aggregateBy\":{\"type\":\"string\",\"enum\":[\"AVG\",\"COUNT\",\"MIN\",\"MAX\",\"SUM\",\"LAST\"],\"description\":\"Aggregation per bucket. Default AVG.\"},"
            + "\"seriesType\":{\"type\":\"string\",\"enum\":[\"TIMESERIES\",\"SCALAR\"],\"description\":\"TIMESERIES (default) or one SCALAR value per grouping.\"},"
            + "\"startTime\":{\"type\":\"string\",\"description\":\"Window start: ISO-8601 or a duration before now such as 1h. Default 1h.\"},"
            + "\"endTime\":{\"type\":\"string\",\"description\":\"Window end: ISO-8601, 'now', or a duration before now. Default now.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Groupings per page. Default 20.\"},"
            + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from a previous swo_query_metric call with the same arguments.\"}"
            + "}}";

        /// <summary>
        /// Maximum data points listed per grouping in the text output; the summary always covers every point. Minimum
        /// 0, maximum 10000, default 60.
        /// </summary>
        public int MaxPointsPerGrouping
        {
            get
            {
                return _MaxPointsPerGrouping;
            }
            set
            {
                _MaxPointsPerGrouping = Math.Clamp(value, 0, 10000);
            }
        }

        #endregion

        #region Private-Members

        private static readonly string[] _Aggregations = new string[] { "AVG", "COUNT", "MIN", "MAX", "SUM", "LAST" };
        private static readonly string[] _SeriesTypes = new string[] { "TIMESERIES", "SCALAR" };
        private int _MaxPointsPerGrouping = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public SwoQueryMetricTool(LoggingConnectorOptions options)
            : base(new ToolDescriptor
            {
                Name = "swo_query_metric",
                Title = "Query SolarWinds Observability metric",
                Description = "Return measurements of one metric over a bounded window, aggregated per bucket, with min, max, average, and last value per grouping. "
                    + "Scope it with entityId or filter, and keep the window short; windows longer than maxTimeRangeHours are rejected.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Per grouping: attributes, summary, and time/value points; structured JSON {name, bucketSizeInSeconds, groupings[], nextCursor}."
            }, options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            string name = arguments.GetRequiredString("name");
            SwoClient client = SwoClient.FromResource(context.Resource);
            TimeRange range = TimeRangeResolver.Resolve(arguments.GetString("startTime"), arguments.GetString("endTime"), Options.DefaultWindow, client.Settings.MaxTimeRangeHours, DateTime.UtcNow);

            string aggregate = (arguments.GetString("aggregateBy") ?? "AVG").Trim().ToUpperInvariant();
            if (!_Aggregations.Contains(aggregate, StringComparer.Ordinal))
                throw new ConnectorException("invalid_argument", "aggregateBy must be one of " + String.Join(", ", _Aggregations) + ".");
            string series = (arguments.GetString("seriesType") ?? "TIMESERIES").Trim().ToUpperInvariant();
            if (!_SeriesTypes.Contains(series, StringComparer.Ordinal))
                throw new ConnectorException("invalid_argument", "seriesType must be TIMESERIES or SCALAR.");

            string? filter = arguments.GetString("filter");
            string? entityId = arguments.GetString("entityId");
            if (String.IsNullOrWhiteSpace(filter) && !String.IsNullOrWhiteSpace(entityId)) filter = "id:[" + entityId.Trim() + "]";

            SwoMetricQuery query = new SwoMetricQuery
            {
                Name = name,
                Filter = String.IsNullOrWhiteSpace(filter) ? null : filter.Trim(),
                GroupBy = arguments.GetString("groupBy"),
                AggregateBy = aggregate,
                SeriesType = series,
                StartTime = range.Start,
                EndTime = range.End,
                PageSize = arguments.GetInt("limit", 20, 1, 1000),
                SkipToken = arguments.GetString("cursor")
            };
            SwoMeasurementsResponse response = await client.QueryMetricAsync(query, token).ConfigureAwait(false);
            string? next = SwoClient.ExtractSkipToken(response.PageInfo == null ? null : response.PageInfo.NextPage);
            if (!response.Groupings!.Any()) next = null;

            StringBuilder sb = new StringBuilder();
            sb.Append("Metric ").Append(name).Append(' ').Append(aggregate).Append(" between ").Append(TimeRange.Format(range.Start)).Append(" and ").Append(TimeRange.Format(range.End));
            sb.Append(", bucket ").Append(response.BucketSizeInSeconds.ToString(CultureInfo.InvariantCulture)).Append(" s, ").Append(response.Groupings!.Count.ToString(CultureInfo.InvariantCulture)).Append(" grouping(s)\n");
            if (!response.Groupings.Any()) sb.Append("No data points. Check the metric name, the filter, and the window.\n");

            foreach (SwoGrouping g in response.Groupings)
            {
                string attrs = g.Attributes == null || !g.Attributes.Any() ? "(all)" : String.Join(", ", g.Attributes.Select(a => a.Key + "=" + a.Value));
                List<SwoMeasurement> points = (g.Measurements ?? new List<SwoMeasurement>()).OrderBy(m => m.Time).ToList();
                List<double> values = points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToList();
                sb.Append("## ").Append(attrs).Append('\n');
                if (values.Any())
                {
                    sb.Append("points ").Append(values.Count.ToString(CultureInfo.InvariantCulture))
                        .Append(", min ").Append(Fmt(values.Min()))
                        .Append(", max ").Append(Fmt(values.Max()))
                        .Append(", avg ").Append(Fmt(values.Average()))
                        .Append(", last ").Append(Fmt(values.Last())).Append('\n');
                }
                else
                {
                    sb.Append("no values\n");
                }

                IEnumerable<SwoMeasurement> listed = points.Count > _MaxPointsPerGrouping ? points.Skip(points.Count - _MaxPointsPerGrouping) : points;
                if (points.Count > _MaxPointsPerGrouping && _MaxPointsPerGrouping > 0) sb.Append("(last ").Append(_MaxPointsPerGrouping.ToString(CultureInfo.InvariantCulture)).Append(" points)\n");
                foreach (SwoMeasurement p in listed)
                    sb.Append(p.Time.HasValue ? TimeRange.Format(p.Time.Value.UtcDateTime) : "-").Append(' ').Append(p.Value.HasValue ? Fmt(p.Value.Value) : "null").Append('\n');
            }
            if (next != null) sb.Append("More groupings are available: call swo_query_metric with the same arguments and cursor \"").Append(next).Append("\".\n");

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            Dictionary<string, object?> structured = new Dictionary<string, object?>
            {
                { "name", name },
                { "aggregateBy", aggregate },
                { "startTime", range.Start },
                { "endTime", range.End },
                { "bucketSizeInSeconds", response.BucketSizeInSeconds },
                { "groupings", response.Groupings },
                { "nextCursor", next }
            };
            result.StructuredJson = LoggingJson.Serialize(structured);
            result.Metrics["groupings"] = response.Groupings.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static string Fmt(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
