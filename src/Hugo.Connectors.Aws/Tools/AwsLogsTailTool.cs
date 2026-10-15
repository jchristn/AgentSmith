namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.CloudWatchLogs.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_logs_tail</c> (Read): logs:FilterLogEvents over a bounded window, keeping the newest events up to the event
    /// limit. The scan stops after <see cref="MaxPages"/> pages and says so. Thread-safe.
    /// </summary>
    public class AwsLogsTailTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_logs_tail";

        /// <summary>
        /// Most FilterLogEvents pages one call reads. Default 20, minimum 1, maximum 200.
        /// </summary>
        public int MaxPages
        {
            get
            {
                return _MaxPages;
            }
            set
            {
                _MaxPages = Math.Clamp(value, 1, 200);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxPages = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsLogsTailTool() : base(BuildDescriptor(), AwsServiceNames.Logs, "logs:FilterLogEvents")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            string group = arguments.GetRequiredString("logGroupName").Trim();
            AwsTimeWindow window = AwsTimeWindow.Resolve(arguments, options, DateTime.UtcNow);
            int limit = Math.Min(arguments.GetInt("limit", options.LogsMaxEvents, 1, 10000), options.LogsMaxEvents);
            limit = Math.Min(limit, Math.Min(context.Limits.MaxLines, context.Limits.MaxRows));

            FilterLogEventsRequest request = new FilterLogEventsRequest();
            request.LogGroupName = group;
            request.StartTime = new DateTimeOffset(window.StartUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
            request.EndTime = new DateTimeOffset(window.EndUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
            string? pattern = arguments.GetString("filterPattern");
            if (!String.IsNullOrWhiteSpace(pattern)) request.FilterPattern = pattern;
            List<string> streams = arguments.GetStringList("logStreamNames");
            string? streamPrefix = arguments.GetString("logStreamNamePrefix");
            if (streams.Count > 0 && !String.IsNullOrWhiteSpace(streamPrefix))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Use logStreamNames or logStreamNamePrefix, not both.");
            if (streams.Count > 0) request.LogStreamNames = streams;
            if (!String.IsNullOrWhiteSpace(streamPrefix)) request.LogStreamNamePrefix = streamPrefix;
            string? next = arguments.GetString("nextToken");
            if (!String.IsNullOrEmpty(next)) request.NextToken = next;

            Queue<FilteredLogEvent> kept = new Queue<FilteredLogEvent>();
            long seen = 0;
            int pages = 0;
            string? continuation = null;
            do
            {
                token.ThrowIfCancellationRequested();
                FilterLogEventsResponse response = await session.Logs().FilterLogEventsAsync(request, token).ConfigureAwait(false);
                foreach (FilteredLogEvent e in response.Events ?? new List<FilteredLogEvent>())
                {
                    seen++;
                    kept.Enqueue(e);
                    while (kept.Count > limit) kept.Dequeue();
                }
                pages++;
                continuation = response.NextToken;
                request.NextToken = continuation;
            }
            while (!String.IsNullOrEmpty(continuation) && pages < _MaxPages);

            List<FilteredLogEvent> events = kept.OrderBy(e => e.Timestamp ?? 0).ToList();
            AwsTable table = new AwsTable("Log events in " + group + " " + window.ToString(), "timestamp", "stream", "message");
            table.RowsInTrailerOnly = true;
            StringBuilder lines = new StringBuilder();
            foreach (FilteredLogEvent e in events)
            {
                string? ts = e.Timestamp == null ? null : AwsResultBuilder.Time(DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp.Value).UtcDateTime);
                string message = (e.Message ?? String.Empty).TrimEnd('\r', '\n');
                table.Add(ts, e.LogStreamName, message);
                lines.Append(ts).Append(' ').Append(e.LogStreamName).Append(' ').Append(message.Replace('\n', ' ')).Append('\n');
            }

            if (seen > events.Count)
                table.Notes.Add("Showing the newest " + events.Count.ToString(CultureInfo.InvariantCulture) + " of " + seen.ToString(CultureInfo.InvariantCulture) + " matching events scanned.");
            if (!String.IsNullOrEmpty(continuation))
            {
                table.Truncated = true;
                table.Notes.Add("The scan stopped after " + pages.ToString(CultureInfo.InvariantCulture) + " pages before reaching the end of the window, so newer events may exist. Narrow the window (startTime closer to endTime) or add a filterPattern.");
            }
            if (events.Count == 0) table.Notes.Add("No events matched in the window.");
            table.Details["logGroupName"] = group;
            table.Details["startTime"] = AwsResultBuilder.Time(window.StartUtc);
            table.Details["endTime"] = AwsResultBuilder.Time(window.EndUtc);
            table.Details["eventsScanned"] = seen.ToString(CultureInfo.InvariantCulture);
            ToolResult bounded = new ToolResult();
            table.Trailer = OutputLimiter.Tail(lines.ToString(), Math.Max(1024, context.Limits.MaxBytes - 4096), Math.Max(1, context.Limits.MaxLines - 20), bounded);
            if (bounded.Truncated) table.Truncated = true;
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Tail CloudWatch log events";
            d.Description =
                "Return the newest log events of one log group within a window (default the resource's lookback), optionally "
                + "limited to streams and a CloudWatch filter pattern such as \"ERROR\" or { $.level = \"error\" }. Output is one "
                + "line per event, oldest first, capped by the event limit. Use aws_logs_insights_query for aggregation.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"logGroupName\":{\"type\":\"string\",\"minLength\":1,\"description\":\"Log group to read, for example /aws/lambda/checkout.\"},"
                + "\"logStreamNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":100,\"description\":\"Only these log streams (up to 100). Do not combine with logStreamNamePrefix.\"},"
                + "\"logStreamNamePrefix\":{\"type\":\"string\",\"description\":\"Only log streams whose names start with this prefix. Do not combine with logStreamNames.\"},"
                + "\"filterPattern\":{\"type\":\"string\",\"description\":\"CloudWatch Logs filter pattern.\"},"
                + "\"lookbackMinutes\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Window length in minutes ending at endTime (default now), used when startTime is not set. Defaults to the resource's defaultLookbackMinutes; the whole window may not exceed maxTimeRangeHours.\"},"
                + "\"startTime\":{\"type\":\"string\",\"description\":\"ISO 8601 start (UTC when no offset).\"},"
                + "\"endTime\":{\"type\":\"string\",\"description\":\"ISO 8601 end. Default now.\"},"
                + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most events returned; capped by the resource's logsMaxEvents.\"},"
                + "\"nextToken\":{\"type\":\"string\",\"description\":\"Continue scanning from a previous call's position.\"}"
                + "},\"required\":[\"logGroupName\"],\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "Log lines (timestamp, stream, message), oldest first.";
            return d;
        }

        #endregion
    }
}
