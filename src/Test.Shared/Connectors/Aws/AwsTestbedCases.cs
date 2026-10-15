namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.CloudWatch;
    using Amazon.CloudWatch.Model;
    using Amazon.CloudWatchLogs;
    using Amazon.CloudWatchLogs.Model;
    using Amazon.EC2;
    using Amazon.EC2.Model;
    using Amazon.Runtime;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws;
    using Touchstone.Core;

    /// <summary>
    /// Testbed cases (<c>testbed.aws</c>) against LocalStack community (docker/testbed/aws): every tool LocalStack serves
    /// (STS, EC2, CloudWatch, CloudWatch Logs, Lambda, Route 53, SQS, SNS) through the real SDK. Fresh metric datapoints
    /// and log events are written before the read cases so the suite does not depend on when the testbed started.
    /// Skipped unless HUGO_TESTBED=1.
    /// </summary>
    public class AwsTestbedCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "testbed.aws";

        /// <summary>
        /// Reason cases are skipped.
        /// </summary>
        public const string SkipReason = "AWS testbed disabled: start docker/testbed/aws/compose.yaml and set HUGO_TESTBED=1";

        /// <summary>
        /// Whether the testbed suite runs.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return Environment.GetEnvironmentVariable("HUGO_TESTBED") == "1";
            }
        }

        /// <summary>
        /// LocalStack endpoint, from HUGO_TESTBED_AWS_PORT (default 28566).
        /// </summary>
        public static string Endpoint
        {
            get
            {
                string? raw = Environment.GetEnvironmentVariable("HUGO_TESTBED_AWS_PORT");
                int port = Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p > 0 && p < 65536 ? p : 28566;
                return "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            }
        }

        #endregion

        #region Private-Members

        private const string LogGroup = "/hugo/testbed/app";
        private readonly string _RunTag = Guid.NewGuid().ToString("N").Substring(0, 10);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("validate", "ValidateAsync accepts the LocalStack resource", Validate));
            cases.Add(Case("test-connection", "TestConnectionAsync authenticates and reports account 000000000000", TestConnection));
            cases.Add(Case("caller-identity", "aws_caller_identity returns the LocalStack account and region", CallerIdentity));
            cases.Add(Case("ec2-describe", "aws_ec2_describe_instances lists the tagged fixtures by tag and state", Ec2Describe));
            cases.Add(Case("ec2-paging", "aws_ec2_describe_instances pages with nextToken without duplicates", Ec2Paging));
            cases.Add(Case("ec2-not-found", "aws_ec2_describe_instances with an unknown ID returns not_found", Ec2NotFound));
            cases.Add(Case("ec2-actions", "aws_ec2_instance_action stop, start, reboot, and terminate change a scratch instance", Ec2Actions));
            cases.Add(Case("cloudwatch-metric-data", "aws_cloudwatch_get_metric_data summarizes fresh datapoints for two statistics", CloudWatchMetricData));
            cases.Add(Case("cloudwatch-alarms", "aws_cloudwatch_list_alarms filters by state and prefix", CloudWatchAlarms));
            cases.Add(Case("logs-tail", "aws_logs_tail returns the newest events and honors a filter pattern", LogsTail));
            cases.Add(Case("logs-tail-limit", "aws_logs_tail keeps only the newest events up to the limit", LogsTailLimit));
            cases.Add(Case("logs-insights", "aws_logs_insights_query starts, polls, and returns rows", LogsInsights));
            cases.Add(Case("logs-missing-group", "aws_logs_tail on a missing log group returns not_found", LogsMissingGroup));
            cases.Add(Case("lambda-list", "aws_lambda_list_functions lists the fixture function", LambdaList));
            cases.Add(Case("lambda-invoke", "aws_lambda_invoke returns the echoed payload and the log tail", LambdaInvoke));
            cases.Add(Case("route53-zones", "aws_route53_list_records without a zone lists hosted zones", Route53Zones));
            cases.Add(Case("route53-records", "aws_route53_list_records by zone name lists A, CNAME, and TXT records", Route53Records));
            cases.Add(Case("sqs-attributes", "aws_sqs_queue_attributes shows depth and the dead-letter target", SqsAttributes));
            cases.Add(Case("sqs-list", "aws_sqs_queue_attributes with a prefix summarizes matching queues", SqsList));
            cases.Add(Case("sns-topics", "aws_sns_list_topics lists the fixture topic with subscription counts", SnsTopics));
            cases.Add(Case("allowed-services", "allowedServices refuses other services against the live endpoint", AllowedServices));
            cases.Add(Case("secret-scrubbing", "No testbed output carries the secret access key", SecretScrubbing));
            return new TestSuiteDescriptor(SuiteId, "AWS testbed: LocalStack", cases, null, null);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            if (!Enabled) return new TestCaseDescriptor(SuiteId, id, name, body, null, true, SkipReason);
            return new TestCaseDescriptor(SuiteId, id, name, body, null, false, null);
        }

        private static ConnectorInvocationContext Context(Dictionary<string, string>? settings = null, TimeSpan? timeout = null)
        {
            return AwsTestSupport.Context(AwsTestSupport.Resource(Endpoint, settings), timeout ?? TimeSpan.FromSeconds(60));
        }

        private static T Configure<T>(T config) where T : ClientConfig
        {
            config.ServiceURL = Endpoint;
            config.AuthenticationRegion = "us-east-1";
            return config;
        }

        private static BasicAWSCredentials Credentials()
        {
            return new BasicAWSCredentials(AwsTestSupport.AccessKeyId, AwsTestSupport.SecretAccessKey);
        }

        private static async Task Validate(CancellationToken token)
        {
            ConnectorValidationResult result = await AwsTestSupport.Connector.ValidateAsync(AwsTestSupport.Resource(Endpoint), token).ConfigureAwait(false);
            AwsAssert.True(result.IsValid, "valid: " + String.Join("; ", result.Errors.Select(e => e.Field + " " + e.Message)));
        }

        private static async Task TestConnection(CancellationToken token)
        {
            ConnectorHealthResult health = await AwsTestSupport.Connector.TestConnectionAsync(AwsTestSupport.Resource(Endpoint), token).ConfigureAwait(false);
            AwsAssert.True(health.Success, "health: " + health.Message);
            AwsAssert.Equal("000000000000", health.Details["account"], "account");
            AwsAssert.Equal(Endpoint, health.Details["endpointOverride"], "endpoint detail");
        }

        private static async Task CallerIdentity(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_caller_identity", "{}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "caller identity");
            AwsAssert.Contains(result.Text, "account: 000000000000", "account");
            AwsAssert.Contains(result.Text, "region: us-east-1", "region");
        }

        private static async Task Ec2Describe(CancellationToken token)
        {
            ConnectorInvocationContext context = Context();
            ToolResult web = await AwsTestSupport.InvokeAsync(context, "aws_ec2_describe_instances", "{\"filters\":[{\"name\":\"tag:Name\",\"values\":[\"hugo-web\"]}],\"states\":[\"running\",\"stopped\"]}", token).ConfigureAwait(false);
            AwsAssert.Ok(web, "describe web");
            AwsAssert.Contains(web.Text, "hugo-web", "web name");
            AwsAssert.Contains(web.Text, "t3.micro", "web type");
            AwsAssert.NotContains(web.Text, "hugo-worker", "worker filtered out");
            AwsAssert.Contains(web.StructuredJson, "\"count\":2", "two web instances");

            ToolResult env = await AwsTestSupport.InvokeAsync(context, "aws_ec2_describe_instances", "{\"filters\":[{\"name\":\"tag:env\",\"values\":[\"testbed\"]}]}", token).ConfigureAwait(false);
            AwsAssert.Ok(env, "describe env");
            AwsAssert.Contains(env.Text, "hugo-worker", "worker listed");
            AwsAssert.Contains(env.Text, "account 000000000000", "account in heading");
        }

        private static async Task Ec2Paging(CancellationToken token)
        {
            ConnectorInvocationContext context = Context();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string? next = null;
            int pages = 0;
            do
            {
                string args = next == null ? "{\"maxResults\":5}" : "{\"maxResults\":5,\"nextToken\":\"" + next + "\"}";
                ToolResult page = await AwsTestSupport.InvokeAsync(context, "aws_ec2_describe_instances", args, token).ConfigureAwait(false);
                AwsAssert.Ok(page, "page " + pages);
                Hugo.Connectors.Aws.Models.AwsOutput? output = AwsJson.Deserialize<Hugo.Connectors.Aws.Models.AwsOutput>(page.StructuredJson!);
                AwsAssert.True(output != null && output.Items != null, "structured items");
                AwsAssert.True(output!.Items!.Count <= 5, "page size honored");
                foreach (Dictionary<string, string?> item in output.Items) AwsAssert.True(seen.Add(item["instanceId"]!), "no duplicate " + item["instanceId"]);
                next = output.NextToken;
                pages++;
            }
            while (next != null && pages < 20);
            AwsAssert.True(seen.Count >= 3, "at least the three fixtures were seen (" + seen.Count + ")");
        }

        private static async Task Ec2NotFound(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_ec2_describe_instances", "{\"instanceIds\":[\"i-0deadbeefdeadbeef\"]}", token).ConfigureAwait(false);
            AwsAssert.Error(result, ConnectorErrorCodes.NotFound, "unknown instance");
        }

        private async Task Ec2Actions(CancellationToken token)
        {
            string id;
            using (AmazonEC2Client ec2 = new AmazonEC2Client(Credentials(), Configure(new AmazonEC2Config())))
            {
                RunInstancesRequest run = new RunInstancesRequest { ImageId = "ami-df5de72bdb3b", InstanceType = InstanceType.T3Nano, MinCount = 1, MaxCount = 1 };
                run.TagSpecifications = new List<TagSpecification> { new TagSpecification { ResourceType = ResourceType.Instance, Tags = new List<Amazon.EC2.Model.Tag> { new Amazon.EC2.Model.Tag("Name", "hugo-scratch-" + _RunTag) } } };
                RunInstancesResponse started = await ec2.RunInstancesAsync(run, token).ConfigureAwait(false);
                id = started.Reservation.Instances[0].InstanceId;
            }

            ConnectorInvocationContext context = Context();
            ToolResult stop = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
            AwsAssert.Ok(stop, "stop");
            AwsAssert.Contains(stop.Text, "| " + id + " | running | stopping |", "stop transition");

            EffectClassification terminateClass = AwsTestSupport.Classify(context, "aws_ec2_instance_action", "{\"action\":\"terminate\",\"instanceIds\":[\"" + id + "\"]}");
            AwsAssert.True(terminateClass.AlwaysRequireApproval, "terminate always requires approval");
            AwsAssert.Contains(terminateClass.ApprovalSummary, "account 000000000000", "summary names the LocalStack account after the first call");
            AwsAssert.Contains(terminateClass.ApprovalSummary, id, "summary names the instance");

            ToolResult start = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"start\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
            AwsAssert.Ok(start, "start");
            ToolResult reboot = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"reboot\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
            AwsAssert.Ok(reboot, "reboot");
            ToolResult terminate = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"terminate\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
            AwsAssert.Ok(terminate, "terminate");

            ToolResult after = await AwsTestSupport.InvokeAsync(context, "aws_ec2_describe_instances", "{\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
            AwsAssert.Ok(after, "describe after terminate");
            AwsAssert.True(after.Text.Contains("terminated", StringComparison.Ordinal) || after.Text.Contains("shutting-down", StringComparison.Ordinal), "instance is terminating: " + after.Text);
        }

        private async Task CloudWatchMetricData(CancellationToken token)
        {
            string service = "run-" + _RunTag;
            using (AmazonCloudWatchClient cw = new AmazonCloudWatchClient(Credentials(), Configure(new AmazonCloudWatchConfig())))
            {
                List<MetricDatum> data = new List<MetricDatum>();
                DateTime now = DateTime.UtcNow;
                for (int i = 1; i <= 5; i++)
                {
                    data.Add(new MetricDatum
                    {
                        MetricName = "RequestCount",
                        Dimensions = new List<Dimension> { new Dimension { Name = "Service", Value = service } },
                        Value = i * 10,
                        Timestamp = now.AddMinutes(-i * 2)
                    });
                }
                await cw.PutMetricDataAsync(new PutMetricDataRequest { Namespace = "Hugo/Testbed", MetricData = data }, token).ConfigureAwait(false);
            }

            // LocalStack community does not evaluate metric math expressions, so only plain metrics are exercised here.
            string args = "{\"lookbackMinutes\":30,\"period\":60,\"metrics\":[{\"id\":\"req\",\"namespace\":\"Hugo/Testbed\",\"metricName\":\"RequestCount\",\"dimensions\":{\"Service\":\"" + service + "\"},\"stat\":\"Sum\"},"
                + "{\"id\":\"peak\",\"namespace\":\"Hugo/Testbed\",\"metricName\":\"RequestCount\",\"dimensions\":{\"Service\":\"" + service + "\"},\"stat\":\"Maximum\",\"label\":\"peak\"}]}";
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_cloudwatch_get_metric_data", args, token).ConfigureAwait(false);
            AwsAssert.Ok(result, "metric data");
            AwsAssert.Contains(result.Text, "| req | Hugo/Testbed RequestCount (Service=" + service + ") | Sum | 5 | 10 | 30 | 50 |", "summary row");
            AwsAssert.Contains(result.Text, "Datapoints (oldest first):", "datapoints");
            AwsAssert.Contains(result.Text, "| peak | peak | Maximum | 5 | 10 | 30 | 50 |", "second metric row");

            ToolResult tooLong = await AwsTestSupport.InvokeAsync(Context(), "aws_cloudwatch_get_metric_data", "{\"lookbackMinutes\":20000,\"metrics\":[{\"namespace\":\"Hugo/Testbed\",\"metricName\":\"RequestCount\"}]}", token).ConfigureAwait(false);
            AwsAssert.Error(tooLong, ConnectorErrorCodes.TimeRangeExceeded, "window too long");
        }

        private async Task CloudWatchAlarms(CancellationToken token)
        {
            // LocalStack re-evaluates alarms against (stale) seed data, so pin the fixture alarm states for this run.
            using (AmazonCloudWatchClient cw = new AmazonCloudWatchClient(Credentials(), Configure(new AmazonCloudWatchConfig())))
            {
                await cw.SetAlarmStateAsync(new SetAlarmStateRequest { AlarmName = "hugo-testbed-high-latency", StateValue = StateValue.ALARM, StateReason = "Run " + _RunTag + ": average latency 560 ms above the 500 ms threshold" }, token).ConfigureAwait(false);
                await cw.SetAlarmStateAsync(new SetAlarmStateRequest { AlarmName = "hugo-testbed-queue-depth", StateValue = StateValue.OK, StateReason = "Run " + _RunTag }, token).ConfigureAwait(false);
            }
            ConnectorInvocationContext context = Context();
            // LocalStack applies namePrefix but then ignores state, so the state filter is checked on its own.
            ToolResult alarm = await AwsTestSupport.InvokeAsync(context, "aws_cloudwatch_list_alarms", "{\"state\":\"ALARM\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(alarm, "alarms in ALARM");
            AwsAssert.Contains(alarm.Text, "hugo-testbed-high-latency", "firing alarm");
            AwsAssert.Contains(alarm.Text, "Average > 500", "condition");
            AwsAssert.Contains(alarm.Text, "560 ms", "state reason");
            AwsAssert.NotContains(alarm.Text, "hugo-testbed-queue-depth", "OK alarm filtered");

            ToolResult all = await AwsTestSupport.InvokeAsync(context, "aws_cloudwatch_list_alarms", "{\"namePrefix\":\"hugo-testbed\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(all, "all alarms");
            AwsAssert.Contains(all.Text, "hugo-testbed-queue-depth", "OK alarm listed");
        }

        private async Task<string> WriteEventsAsync(string stream, IList<string> messages, CancellationToken token)
        {
            using (AmazonCloudWatchLogsClient logs = new AmazonCloudWatchLogsClient(Credentials(), Configure(new AmazonCloudWatchLogsConfig())))
            {
                try
                {
                    await logs.CreateLogStreamAsync(new CreateLogStreamRequest { LogGroupName = LogGroup, LogStreamName = stream }, token).ConfigureAwait(false);
                }
                catch (ResourceAlreadyExistsException)
                {
                }
                DateTime now = DateTime.UtcNow.AddSeconds(-messages.Count - 5);
                List<InputLogEvent> events = new List<InputLogEvent>();
                for (int i = 0; i < messages.Count; i++) events.Add(new InputLogEvent { Message = messages[i], Timestamp = now.AddSeconds(i) });
                await logs.PutLogEventsAsync(new PutLogEventsRequest { LogGroupName = LogGroup, LogStreamName = stream, LogEvents = events }, token).ConfigureAwait(false);
            }
            return stream;
        }

        private async Task LogsTail(CancellationToken token)
        {
            string stream = "tail-" + _RunTag;
            await WriteEventsAsync(stream, new List<string> { "INFO " + _RunTag + " boot", "ERROR " + _RunTag + " database timeout", "INFO " + _RunTag + " recovered" }, token).ConfigureAwait(false);

            ToolResult all = await AwsTestSupport.InvokeAsync(Context(), "aws_logs_tail", "{\"logGroupName\":\"" + LogGroup + "\",\"logStreamNames\":[\"" + stream + "\"],\"lookbackMinutes\":30}", token).ConfigureAwait(false);
            AwsAssert.Ok(all, "tail");
            AwsAssert.Contains(all.Text, stream + " ERROR " + _RunTag + " database timeout", "error line");
            AwsAssert.True(all.Text.IndexOf("boot", StringComparison.Ordinal) < all.Text.IndexOf("recovered", StringComparison.Ordinal), "oldest first");

            // LocalStack community accepts filterPattern and logStreamNamePrefix but does not apply them, so this only
            // checks the call succeeds and still returns the matching event; the filtering itself is AWS behavior.
            ToolResult filtered = await AwsTestSupport.InvokeAsync(Context(), "aws_logs_tail", "{\"logGroupName\":\"" + LogGroup + "\",\"logStreamNamePrefix\":\"tail-\",\"filterPattern\":\"ERROR\",\"lookbackMinutes\":30}", token).ConfigureAwait(false);
            AwsAssert.Ok(filtered, "filtered tail");
            AwsAssert.Contains(filtered.Text, "database timeout", "filtered error");
        }

        private async Task LogsTailLimit(CancellationToken token)
        {
            string stream = "limit-" + _RunTag;
            List<string> messages = Enumerable.Range(1, 12).Select(i => "line " + i.ToString("00", CultureInfo.InvariantCulture) + " " + _RunTag).ToList();
            await WriteEventsAsync(stream, messages, token).ConfigureAwait(false);
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_logs_tail", "{\"logGroupName\":\"" + LogGroup + "\",\"logStreamNames\":[\"" + stream + "\"],\"lookbackMinutes\":30,\"limit\":5}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "limited tail");
            AwsAssert.Contains(result.Text, "line 12 ", "newest kept");
            AwsAssert.Contains(result.Text, "line 08 ", "fifth newest kept");
            AwsAssert.NotContains(result.Text, "line 07 ", "older dropped");
            AwsAssert.Contains(result.Text, "Showing the newest 5 of 12", "note");
        }

        private async Task LogsInsights(CancellationToken token)
        {
            string stream = "insights-" + _RunTag;
            await WriteEventsAsync(stream, new List<string> { "ERROR " + _RunTag + " one", "ERROR " + _RunTag + " two" }, token).ConfigureAwait(false);
            string args = "{\"logGroupNames\":[\"" + LogGroup + "\"],\"query\":\"fields @timestamp, @message | filter @message like /" + _RunTag + "/ | limit 50\",\"lookbackMinutes\":30,\"timeoutSeconds\":20}";
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_logs_insights_query", args, token).ConfigureAwait(false);
            AwsAssert.Ok(result, "insights");
            AwsAssert.Contains(result.Text, "(Complete)", "query completed");
            AwsAssert.Contains(result.Text, "@message", "message column");
            AwsAssert.Contains(result.StructuredJson, "\"queryId\"", "query id returned");

            Hugo.Connectors.Aws.Models.AwsOutput? output = AwsJson.Deserialize<Hugo.Connectors.Aws.Models.AwsOutput>(result.StructuredJson!);
            string queryId = output!.Details!["queryId"]!;
            ToolResult again = await AwsTestSupport.InvokeAsync(Context(), "aws_logs_insights_query", "{\"logGroupNames\":[\"" + LogGroup + "\"],\"query\":\"fields @message\",\"queryId\":\"" + queryId + "\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(again, "poll by query id");
            AwsAssert.Contains(again.Text, queryId, "same query");
        }

        private static async Task LogsMissingGroup(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_logs_tail", "{\"logGroupName\":\"/hugo/does-not-exist\"}", token).ConfigureAwait(false);
            AwsAssert.Error(result, ConnectorErrorCodes.NotFound, "missing log group");
        }

        private static async Task LambdaList(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_lambda_list_functions", "{\"nameContains\":\"hugo-testbed\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "lambda list");
            AwsAssert.Contains(result.Text, "| hugo-testbed-echo | python3.12 | 128 | 30 |", "fixture function");
        }

        private static async Task LambdaInvoke(CancellationToken token)
        {
            ConnectorInvocationContext context = Context(null, TimeSpan.FromSeconds(180));
            ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_lambda_invoke", "{\"functionName\":\"hugo-testbed-echo\",\"payload\":{\"order\":1001}}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "invoke");
            AwsAssert.Contains(result.Text, "statusCode: 200", "status");
            AwsAssert.Contains(result.Text, "\"order\": 1001", "echoed payload");
            AwsAssert.Contains(result.Text, "hugo testbed invoked", "log tail");

            ToolResult async = await AwsTestSupport.InvokeAsync(context, "aws_lambda_invoke", "{\"functionName\":\"hugo-testbed-echo\",\"payload\":{\"order\":1002},\"invocationType\":\"Event\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(async, "async invoke");
            AwsAssert.Contains(async.Text, "statusCode: 202", "accepted");

            ToolResult tooBig = await AwsTestSupport.InvokeAsync(Context(new Dictionary<string, string> { { "lambdaMaxPayloadBytes", "16" } }), "aws_lambda_invoke", "{\"functionName\":\"hugo-testbed-echo\",\"payload\":{\"blob\":\"0123456789abcdef\"}}", token).ConfigureAwait(false);
            AwsAssert.Error(tooBig, ConnectorErrorCodes.TooLarge, "payload cap");

            ToolResult missing = await AwsTestSupport.InvokeAsync(context, "aws_lambda_invoke", "{\"functionName\":\"hugo-no-such-function\"}", token).ConfigureAwait(false);
            AwsAssert.Error(missing, ConnectorErrorCodes.NotFound, "missing function");
        }

        private static async Task Route53Zones(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_route53_list_records", "{}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "zones");
            AwsAssert.Contains(result.Text, "testbed.hugo.internal.", "fixture zone");
        }

        private static async Task Route53Records(CancellationToken token)
        {
            ConnectorInvocationContext context = Context();
            ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_route53_list_records", "{\"zoneName\":\"testbed.hugo.internal\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "records");
            AwsAssert.Contains(result.Text, "| www.testbed.hugo.internal. | A | 300 | 10.0.0.10 10.0.0.11 |", "A record");
            AwsAssert.Contains(result.Text, "| api.testbed.hugo.internal. | CNAME | 60 | www.testbed.hugo.internal |", "CNAME record");
            AwsAssert.Contains(result.Text, "TXT", "TXT record");

            ToolResult onlyA = await AwsTestSupport.InvokeAsync(context, "aws_route53_list_records", "{\"zoneName\":\"testbed.hugo.internal.\",\"type\":\"A\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(onlyA, "records filtered");
            AwsAssert.NotContains(onlyA.Text, "CNAME", "type filter");

            ToolResult paged = await AwsTestSupport.InvokeAsync(context, "aws_route53_list_records", "{\"zoneName\":\"testbed.hugo.internal\",\"maxResults\":1}", token).ConfigureAwait(false);
            AwsAssert.Ok(paged, "records page 1");
            Hugo.Connectors.Aws.Models.AwsOutput? output = AwsJson.Deserialize<Hugo.Connectors.Aws.Models.AwsOutput>(paged.StructuredJson!);
            AwsAssert.True(output != null && output.NextToken != null, "next token on a one-record page");
            ToolResult page2 = await AwsTestSupport.InvokeAsync(context, "aws_route53_list_records", "{\"zoneName\":\"testbed.hugo.internal\",\"maxResults\":1,\"nextToken\":\"" + output!.NextToken + "\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(page2, "records page 2");
            AwsAssert.True(page2.StructuredJson != paged.StructuredJson, "second page differs");

            ToolResult missing = await AwsTestSupport.InvokeAsync(context, "aws_route53_list_records", "{\"zoneName\":\"nope.hugo.internal\"}", token).ConfigureAwait(false);
            AwsAssert.Error(missing, ConnectorErrorCodes.NotFound, "missing zone");
        }

        private static async Task SqsAttributes(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_sqs_queue_attributes", "{\"queueName\":\"hugo-testbed-orders\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "queue attributes");
            AwsAssert.Contains(result.Text, "| ApproximateNumberOfMessages |", "depth attribute");
            AwsAssert.Contains(result.Text, "| VisibilityTimeout | 45 |", "visibility timeout");
            AwsAssert.Contains(result.Text, "hugo-testbed-orders-dlq", "redrive target");

            ToolResult missing = await AwsTestSupport.InvokeAsync(Context(), "aws_sqs_queue_attributes", "{\"queueName\":\"hugo-no-such-queue\"}", token).ConfigureAwait(false);
            AwsAssert.Error(missing, ConnectorErrorCodes.NotFound, "missing queue");
        }

        private static async Task SqsList(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_sqs_queue_attributes", "{\"queueNamePrefix\":\"hugo-testbed\"}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "queue list");
            AwsAssert.Contains(result.Text, "| hugo-testbed-orders |", "orders queue");
            AwsAssert.Contains(result.Text, "| hugo-testbed-orders-dlq |", "dlq");
            AwsAssert.Contains(result.Text, "hugo-testbed-orders-dlq |\n", "dead letter column");
        }

        private static async Task SnsTopics(CancellationToken token)
        {
            ToolResult result = await AwsTestSupport.InvokeAsync(Context(), "aws_sns_list_topics", "{\"nameContains\":\"hugo-testbed\",\"includeAttributes\":true}", token).ConfigureAwait(false);
            AwsAssert.Ok(result, "topics");
            AwsAssert.Contains(result.Text, "| hugo-testbed-alerts | arn:aws:sns:us-east-1:000000000000:hugo-testbed-alerts |", "topic row");
            AwsAssert.Contains(result.StructuredJson, "\"subscriptions\":\"", "subscription count fetched");
            AwsAssert.Contains(result.Text, "HugoAlerts", "display name");
        }

        private static async Task AllowedServices(CancellationToken token)
        {
            ConnectorInvocationContext context = Context(new Dictionary<string, string> { { "allowedServices", "logs" } });
            ToolResult ec2 = await AwsTestSupport.InvokeAsync(context, "aws_ec2_describe_instances", "{}", token).ConfigureAwait(false);
            AwsAssert.Error(ec2, ConnectorErrorCodes.PermissionDenied, "ec2 refused");
            ToolResult identity = await AwsTestSupport.InvokeAsync(context, "aws_caller_identity", "{}", token).ConfigureAwait(false);
            AwsAssert.Ok(identity, "identity always allowed");
            ToolResult logs = await AwsTestSupport.InvokeAsync(context, "aws_logs_tail", "{\"logGroupName\":\"" + LogGroup + "\",\"lookbackMinutes\":5}", token).ConfigureAwait(false);
            AwsAssert.Ok(logs, "logs allowed");
        }

        private static async Task SecretScrubbing(CancellationToken token)
        {
            ConnectorInvocationContext context = Context();
            foreach (string tool in new string[] { "aws_caller_identity", "aws_ec2_describe_instances", "aws_cloudwatch_list_alarms", "aws_sqs_queue_attributes", "aws_sns_list_topics", "aws_route53_list_records", "aws_lambda_list_functions" })
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(context, tool, "{}", token).ConfigureAwait(false);
                AwsTestSupport.AssertNoSecret(result, tool);
            }
        }

        #endregion
    }
}
