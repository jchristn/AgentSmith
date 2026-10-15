namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.CloudWatch;
    using Amazon.CloudWatch.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_cloudwatch_list_alarms</c> (Read): cloudwatch:DescribeAlarms filtered by state and name prefix, with paging.
    /// Thread-safe.
    /// </summary>
    public class AwsCloudWatchListAlarmsTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_cloudwatch_list_alarms";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsCloudWatchListAlarmsTool() : base(BuildDescriptor(), AwsServiceNames.CloudWatch, "cloudwatch:DescribeAlarms")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            DescribeAlarmsRequest request = new DescribeAlarmsRequest();
            string? state = arguments.GetString("state");
            if (!String.IsNullOrWhiteSpace(state)) request.StateValue = new StateValue(state.Trim().ToUpperInvariant());
            string? prefix = arguments.GetString("namePrefix");
            if (!String.IsNullOrWhiteSpace(prefix)) request.AlarmNamePrefix = prefix.Trim();
            List<string> names = arguments.GetStringList("alarmNames");
            if (names.Count > 0) request.AlarmNames = names;
            request.MaxRecords = PageSize(context, arguments, options, 1, 100);
            request.AlarmTypes = new List<string> { "MetricAlarm", "CompositeAlarm" };
            string? next = arguments.GetString("nextToken");
            if (!String.IsNullOrEmpty(next)) request.NextToken = next;

            DescribeAlarmsResponse response = await session.CloudWatch().DescribeAlarmsAsync(request, token).ConfigureAwait(false);
            AwsTable table = new AwsTable("CloudWatch alarms", "alarm", "state", "since", "metric", "condition", "reason");
            foreach (MetricAlarm a in response.MetricAlarms ?? new List<MetricAlarm>())
            {
                string metric = String.IsNullOrEmpty(a.MetricName)
                    ? "(metric math)"
                    : a.Namespace + " " + a.MetricName + ((a.Dimensions ?? new List<Dimension>()).Count > 0 ? " (" + String.Join(", ", a.Dimensions!.Select(dm => dm.Name + "=" + dm.Value)) + ")" : String.Empty);
                string condition = (a.Statistic?.Value ?? a.ExtendedStatistic ?? "value") + " " + Operator(a.ComparisonOperator?.Value) + " " + AwsResultBuilder.Number(a.Threshold)
                    + " for " + AwsResultBuilder.Number(a.EvaluationPeriods) + "x" + AwsResultBuilder.Number(a.Period) + "s";
                table.Add(a.AlarmName, a.StateValue?.Value, AwsResultBuilder.Time(a.StateUpdatedTimestamp), metric, condition, Shorten(a.StateReason, 200));
            }
            foreach (CompositeAlarm c in response.CompositeAlarms ?? new List<CompositeAlarm>())
                table.Add(c.AlarmName, c.StateValue?.Value, AwsResultBuilder.Time(c.StateUpdatedTimestamp), "(composite)", Shorten(c.AlarmRule, 120), Shorten(c.StateReason, 200));
            table.NextToken = response.NextToken;
            if (table.Rows.Count == 0) table.Notes.Add("No alarms matched.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static string Operator(string? op)
        {
            switch (op)
            {
                case "GreaterThanOrEqualToThreshold":
                    return ">=";
                case "GreaterThanThreshold":
                    return ">";
                case "LessThanThreshold":
                    return "<";
                case "LessThanOrEqualToThreshold":
                    return "<=";
                default:
                    return op ?? "?";
            }
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "List CloudWatch alarms";
            d.Description =
                "List CloudWatch metric and composite alarms with state, when the state changed, the metric, the condition, and "
                + "the state reason. Filter with state (ALARM, OK, INSUFFICIENT_DATA), namePrefix, or alarmNames. Start with "
                + "state=ALARM when investigating an incident. Paged with nextToken.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"state\":{\"type\":\"string\",\"enum\":[\"ALARM\",\"OK\",\"INSUFFICIENT_DATA\"]},"
                + "\"namePrefix\":{\"type\":\"string\",\"description\":\"Alarm name prefix.\"},"
                + "\"alarmNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":100},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100},"
                + "\"nextToken\":{\"type\":\"string\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of alarms and a nextToken when more pages exist.";
            return d;
        }

        #endregion
    }
}
