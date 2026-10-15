namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.CloudWatchLogs;
    using Amazon.CloudWatchLogs.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_logs_insights_query</c> (Read): starts a CloudWatch Logs Insights query over a bounded window, polls until it
    /// completes or the wait limit passes, and returns the rows (capped by the output limits). A query still running at the
    /// wait limit is reported with its ID so the caller can resume polling with <c>queryId</c>. Thread-safe.
    /// </summary>
    public class AwsLogsInsightsQueryTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_logs_insights_query";

        /// <summary>
        /// Delay between polls. Default 1 second, minimum 100 milliseconds, maximum 10 seconds.
        /// </summary>
        public TimeSpan PollInterval
        {
            get
            {
                return _PollInterval;
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(100)) value = TimeSpan.FromMilliseconds(100);
                if (value > TimeSpan.FromSeconds(10)) value = TimeSpan.FromSeconds(10);
                _PollInterval = value;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _PollInterval = TimeSpan.FromSeconds(1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsLogsInsightsQueryTool() : base(BuildDescriptor(), AwsServiceNames.Logs, "logs:StartQuery", "logs:GetQueryResults")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCore(ConnectorInvocationContext context, ToolArguments arguments, AwsOptions options)
        {
            string query = arguments.GetString("query") ?? String.Empty;
            List<string> groups = arguments.GetStringList("logGroupNames");
            string summary = String.IsNullOrEmpty(arguments.GetString("queryId"))
                ? "Run Logs Insights query on " + (groups.Count == 0 ? "(no log groups)" : String.Join(", ", groups)) + " in " + Where(context) + ": " + Shorten(query, 200)
                : "Poll Logs Insights query " + arguments.GetString("queryId") + " in " + Where(context);
            return new EffectClassification(EffectEnum.Read, "Logs Insights queries only read log data (logs:StartQuery, logs:GetQueryResults)", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            IAmazonCloudWatchLogs logs = session.Logs();
            string? queryId = arguments.GetString("queryId");
            int limit = Math.Clamp(arguments.GetInt("limit", Math.Min(context.Limits.MaxRows, 1000), 1, 10000), 1, 10000);
            AwsTimeWindow? window = null;

            if (String.IsNullOrWhiteSpace(queryId))
            {
                string query = arguments.GetRequiredString("query");
                List<string> groups = arguments.GetStringList("logGroupNames").Select(g => g.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                if (groups.Count == 0) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "logGroupNames needs at least one log group.");
                if (groups.Count > 50) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "At most 50 log groups per query.");
                window = AwsTimeWindow.Resolve(arguments, options, DateTime.UtcNow);

                StartQueryRequest start = new StartQueryRequest();
                start.LogGroupNames = groups;
                start.QueryString = query;
                start.StartTime = new DateTimeOffset(window.StartUtc, TimeSpan.Zero).ToUnixTimeSeconds();
                start.EndTime = new DateTimeOffset(window.EndUtc, TimeSpan.Zero).ToUnixTimeSeconds();
                start.Limit = limit;
                StartQueryResponse started = await logs.StartQueryAsync(start, token).ConfigureAwait(false);
                queryId = started.QueryId;
                if (String.IsNullOrEmpty(queryId)) throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "logs:StartQuery returned no query ID.");
            }

            int waitSeconds = Math.Min(arguments.GetInt("timeoutSeconds", options.LogsQueryTimeoutSeconds, 1, 300), options.LogsQueryTimeoutSeconds);
            TimeSpan wait = TimeSpan.FromSeconds(waitSeconds);
            TimeSpan callBudget = context.Timeout - TimeSpan.FromSeconds(2);
            if (callBudget < TimeSpan.FromSeconds(1)) callBudget = TimeSpan.FromSeconds(1);
            if (wait > callBudget) wait = callBudget;

            DateTime deadline = DateTime.UtcNow + wait;
            GetQueryResultsResponse results;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                results = await logs.GetQueryResultsAsync(new GetQueryResultsRequest { QueryId = queryId }, token).ConfigureAwait(false);
                string status = results.Status?.Value ?? "Unknown";
                if (status != "Running" && status != "Scheduled") break;
                if (DateTime.UtcNow + _PollInterval > deadline) break;
                await Task.Delay(_PollInterval, token).ConfigureAwait(false);
            }

            string finalStatus = results.Status?.Value ?? "Unknown";
            List<List<ResultField>> rows = results.Results ?? new List<List<ResultField>>();
            List<string> columns = new List<string>();
            foreach (List<ResultField> row in rows)
                foreach (ResultField f in row ?? new List<ResultField>())
                    if (!String.IsNullOrEmpty(f.Field) && f.Field != "@ptr" && !columns.Contains(f.Field)) columns.Add(f.Field);

            AwsTable table = new AwsTable("Logs Insights query " + queryId + " (" + finalStatus + ")", columns.ToArray());
            foreach (List<ResultField> row in rows)
            {
                Dictionary<string, string?> map = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (ResultField f in row ?? new List<ResultField>()) if (!String.IsNullOrEmpty(f.Field)) map[f.Field] = f.Value;
                table.Rows.Add(columns.Select(c => map.TryGetValue(c, out string? v) ? v : null).ToList());
            }

            table.Details["queryId"] = queryId;
            table.Details["status"] = finalStatus;
            if (window != null)
            {
                table.Details["startTime"] = AwsResultBuilder.Time(window.StartUtc);
                table.Details["endTime"] = AwsResultBuilder.Time(window.EndUtc);
            }
            if (results.Statistics != null)
            {
                table.Details["recordsMatched"] = AwsResultBuilder.Number(results.Statistics.RecordsMatched);
                table.Details["recordsScanned"] = AwsResultBuilder.Number(results.Statistics.RecordsScanned);
                table.Details["bytesScanned"] = AwsResultBuilder.Number(results.Statistics.BytesScanned);
                table.Notes.Add("Matched " + (AwsResultBuilder.Number(results.Statistics.RecordsMatched) ?? "?") + " of " + (AwsResultBuilder.Number(results.Statistics.RecordsScanned) ?? "?") + " records scanned (" + (AwsResultBuilder.Number(results.Statistics.BytesScanned) ?? "?") + " bytes).");
            }
            if (finalStatus == "Running" || finalStatus == "Scheduled")
                table.Notes.Add("The query is still " + finalStatus.ToLowerInvariant() + " after " + ((int)wait.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " seconds; rows so far are partial. Call again with queryId \"" + queryId + "\" to keep waiting.");
            else if (finalStatus != "Complete")
                table.Notes.Add("The query ended with status " + finalStatus + ".");
            if (rows.Count >= limit) table.Notes.Add("The result hit the row limit of " + limit.ToString(CultureInfo.InvariantCulture) + "; aggregate with stats or narrow the filter.");
            if (table.Rows.Count == 0 && finalStatus == "Complete") table.Notes.Add("The query matched no records.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Run a CloudWatch Logs Insights query";
            d.Description =
                "Run a CloudWatch Logs Insights query over named log groups and an explicit window, wait for it (bounded), and "
                + "return the rows. Prefer aggregations, for example: fields @timestamp, @message | filter @message like /ERROR/ | "
                + "stats count(*) by bin(5m). Keep windows short; queries are billed by bytes scanned. If the query is still "
                + "running when the wait ends, call again with the returned queryId.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"logGroupNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":50,\"description\":\"Log groups to query.\"},"
                + "\"query\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":10000,\"description\":\"Logs Insights query text.\"},"
                + "\"lookbackMinutes\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Window length in minutes ending at endTime (default now), used when startTime is not set. Defaults to the resource's defaultLookbackMinutes; the whole window may not exceed maxTimeRangeHours.\"},"
                + "\"startTime\":{\"type\":\"string\",\"description\":\"ISO 8601 start (UTC when no offset).\"},"
                + "\"endTime\":{\"type\":\"string\",\"description\":\"ISO 8601 end. Default now.\"},"
                + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most rows the query returns.\"},"
                + "\"timeoutSeconds\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":300,\"description\":\"How long to wait; capped by the resource's logsQueryTimeoutSeconds.\"},"
                + "\"queryId\":{\"type\":\"string\",\"description\":\"Resume waiting for a query started earlier instead of starting a new one. Send the same logGroupNames and query along with it; they are not run again.\"}"
                + "},\"required\":[\"logGroupNames\",\"query\"],\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "Query status, statistics, and result rows.";
            return d;
        }

        #endregion
    }
}
