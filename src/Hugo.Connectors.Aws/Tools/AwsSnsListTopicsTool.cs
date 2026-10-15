namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.SimpleNotificationService;
    using Amazon.SimpleNotificationService.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_sns_list_topics</c> (Read): sns:ListTopics with paging, optionally with subscription counts from
    /// sns:GetTopicAttributes. Thread-safe.
    /// </summary>
    public class AwsSnsListTopicsTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_sns_list_topics";

        /// <summary>
        /// Most topics whose attributes are fetched in one call. Default 25, minimum 1, maximum 100.
        /// </summary>
        public int MaxAttributeLookups
        {
            get
            {
                return _MaxAttributeLookups;
            }
            set
            {
                _MaxAttributeLookups = Math.Clamp(value, 1, 100);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxAttributeLookups = 25;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsSnsListTopicsTool() : base(BuildDescriptor(), AwsServiceNames.Sns, "sns:ListTopics", "sns:GetTopicAttributes")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            IAmazonSimpleNotificationService sns = session.Sns();
            ListTopicsRequest request = new ListTopicsRequest();
            string? next = arguments.GetString("nextToken");
            if (!String.IsNullOrEmpty(next)) request.NextToken = next;
            ListTopicsResponse response = await sns.ListTopicsAsync(request, token).ConfigureAwait(false);

            string? contains = arguments.GetString("nameContains");
            bool details = arguments.GetBool("includeAttributes", false);
            List<Topic> topics = (response.Topics ?? new List<Topic>())
                .Where(t => String.IsNullOrWhiteSpace(contains) || NameOf(t.TopicArn).IndexOf(contains.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            AwsTable table = new AwsTable("SNS topics", "topic", "arn", "subscriptions", "pending", "displayName");
            int lookups = 0;
            foreach (Topic t in topics)
            {
                string? confirmed = null;
                string? pending = null;
                string? display = null;
                if (details && lookups < _MaxAttributeLookups)
                {
                    token.ThrowIfCancellationRequested();
                    lookups++;
                    GetTopicAttributesResponse attrs = await sns.GetTopicAttributesAsync(new GetTopicAttributesRequest { TopicArn = t.TopicArn }, token).ConfigureAwait(false);
                    Dictionary<string, string> map = attrs.Attributes ?? new Dictionary<string, string>();
                    confirmed = map.TryGetValue("SubscriptionsConfirmed", out string? c) ? c : null;
                    pending = map.TryGetValue("SubscriptionsPending", out string? p) ? p : null;
                    display = map.TryGetValue("DisplayName", out string? dn) && !String.IsNullOrEmpty(dn) ? dn : null;
                }
                table.Add(NameOf(t.TopicArn), t.TopicArn, confirmed, pending, display);
            }
            table.NextToken = response.NextToken;
            if (details && topics.Count > lookups) table.Notes.Add("Attributes were fetched for the first " + lookups + " topics only.");
            if (table.Rows.Count == 0) table.Notes.Add("No topics matched on this page.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static string NameOf(string? arn)
        {
            if (String.IsNullOrEmpty(arn)) return String.Empty;
            int colon = arn.LastIndexOf(':');
            return colon >= 0 ? arn.Substring(colon + 1) : arn;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "List SNS topics";
            d.Description =
                "List SNS topics (name and ARN), 100 per page. includeAttributes adds confirmed and pending subscription counts "
                + "and the display name. nameContains filters each page by a substring. Paged with nextToken.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"nameContains\":{\"type\":\"string\"},"
                + "\"includeAttributes\":{\"type\":\"boolean\",\"description\":\"Fetch subscription counts. Default false.\"},"
                + "\"nextToken\":{\"type\":\"string\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of topics and a nextToken when more pages exist.";
            return d;
        }

        #endregion
    }
}
