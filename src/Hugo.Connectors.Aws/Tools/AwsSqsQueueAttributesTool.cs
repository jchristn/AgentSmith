namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_sqs_queue_attributes</c> (Read): sqs:GetQueueAttributes for one queue (by URL or name), or a backlog summary
    /// of the queues matching a name prefix (sqs:ListQueues). Never receives or deletes messages. Thread-safe.
    /// </summary>
    public class AwsSqsQueueAttributesTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_sqs_queue_attributes";

        /// <summary>
        /// Most queues summarized when listing by prefix. Default 20, minimum 1, maximum 1000.
        /// </summary>
        public int MaxQueuesListed
        {
            get
            {
                return _MaxQueuesListed;
            }
            set
            {
                _MaxQueuesListed = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private static readonly List<string> _SummaryAttributes = new List<string>
        {
            "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible", "ApproximateNumberOfMessagesDelayed", "RedrivePolicy"
        };

        private int _MaxQueuesListed = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsSqsQueueAttributesTool() : base(BuildDescriptor(), AwsServiceNames.Sqs, "sqs:GetQueueAttributes", "sqs:GetQueueUrl", "sqs:ListQueues")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            IAmazonSQS sqs = session.Sqs();
            string? url = arguments.GetString("queueUrl");
            string? name = arguments.GetString("queueName");

            if (String.IsNullOrWhiteSpace(url) && !String.IsNullOrWhiteSpace(name))
            {
                GetQueueUrlResponse resolved = await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = name.Trim() }, token).ConfigureAwait(false);
                url = resolved.QueueUrl;
            }

            if (!String.IsNullOrWhiteSpace(url))
            {
                GetQueueAttributesResponse attrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = url.Trim(), AttributeNames = new List<string> { "All" } }, token).ConfigureAwait(false);
                AwsTable table = new AwsTable("SQS queue " + QueueName(url), "attribute", "value");
                foreach (KeyValuePair<string, string> kv in (attrs.Attributes ?? new Dictionary<string, string>()).OrderBy(k => k.Key, StringComparer.Ordinal))
                    table.Add(kv.Key, Shorten(FormatValue(kv.Key, kv.Value), 400));
                table.Details["queueUrl"] = url.Trim();
                return Render(context, session, table);
            }

            ListQueuesRequest list = new ListQueuesRequest();
            string? prefix = arguments.GetString("queueNamePrefix");
            if (!String.IsNullOrWhiteSpace(prefix)) list.QueueNamePrefix = prefix.Trim();
            list.MaxResults = Math.Min(PageSize(context, arguments, options, 1, 1000), _MaxQueuesListed);
            string? next = arguments.GetString("nextToken");
            if (!String.IsNullOrEmpty(next)) list.NextToken = next;
            ListQueuesResponse queues = await sqs.ListQueuesAsync(list, token).ConfigureAwait(false);

            AwsTable summary = new AwsTable("SQS queues", "queue", "visible", "inFlight", "delayed", "deadLetterTarget");
            foreach (string q in (queues.QueueUrls ?? new List<string>()).Take(_MaxQueuesListed))
            {
                token.ThrowIfCancellationRequested();
                GetQueueAttributesResponse a = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = q, AttributeNames = _SummaryAttributes }, token).ConfigureAwait(false);
                Dictionary<string, string> map = a.Attributes ?? new Dictionary<string, string>();
                summary.Add(
                    QueueName(q),
                    Value(map, "ApproximateNumberOfMessages"),
                    Value(map, "ApproximateNumberOfMessagesNotVisible"),
                    Value(map, "ApproximateNumberOfMessagesDelayed"),
                    DeadLetterTarget(Value(map, "RedrivePolicy")));
            }
            summary.NextToken = queues.NextToken;
            if (summary.Rows.Count == 0) summary.Notes.Add("No queues matched.");
            else summary.Notes.Add("Pass queueName or queueUrl for every attribute of one queue.");
            return Render(context, session, summary);
        }

        #endregion

        #region Private-Methods

        private static string? Value(Dictionary<string, string> map, string key)
        {
            return map.TryGetValue(key, out string? v) ? v : null;
        }

        private static string QueueName(string url)
        {
            string trimmed = url.Trim().TrimEnd('/');
            int slash = trimmed.LastIndexOf('/');
            return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
        }

        private static string? DeadLetterTarget(string? redrivePolicy)
        {
            if (String.IsNullOrEmpty(redrivePolicy)) return null;
            int at = redrivePolicy.IndexOf("deadLetterTargetArn", StringComparison.Ordinal);
            if (at < 0) return Shorten(redrivePolicy, 120);
            int arn = redrivePolicy.IndexOf("arn:", at, StringComparison.Ordinal);
            if (arn < 0) return Shorten(redrivePolicy, 120);
            int end = redrivePolicy.IndexOf('"', arn);
            string full = end > arn ? redrivePolicy.Substring(arn, end - arn) : redrivePolicy.Substring(arn);
            int colon = full.LastIndexOf(':');
            return colon >= 0 ? full.Substring(colon + 1) : full;
        }

        private static string FormatValue(string key, string value)
        {
            if ((key == "CreatedTimestamp" || key == "LastModifiedTimestamp") && Int64.TryParse(value, out long seconds))
                return AwsResultBuilder.Time(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime) ?? value;
            return value;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "SQS queue attributes";
            d.Description =
                "Show SQS queue attributes without touching messages: for one queue (queueName or queueUrl) every attribute "
                + "including approximate visible, in-flight, and delayed message counts, visibility timeout, retention, and the "
                + "redrive (dead-letter) policy; or, with queueNamePrefix or nothing, a backlog summary of matching queues.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"queueName\":{\"type\":\"string\"},"
                + "\"queueUrl\":{\"type\":\"string\"},"
                + "\"queueNamePrefix\":{\"type\":\"string\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000},"
                + "\"nextToken\":{\"type\":\"string\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "Attribute table for one queue, or a backlog summary table.";
            return d;
        }

        #endregion
    }
}
