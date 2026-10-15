namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws;
    using Touchstone.Core;

    /// <summary>
    /// Stub-backed cases (<c>connectors.aws.stubs</c>): the tools LocalStack community cannot serve (RDS, ELBv2, ECS, Auto
    /// Scaling), STS identity and role assumption, EC2 instance actions, and SDK error mapping, all through the real AWS
    /// SDK against <see cref="AwsStubServer"/> via <c>endpointOverride</c>. Each case starts its own stub. Always runs.
    /// </summary>
    public class AwsStubCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "connectors.aws.stubs";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("test-connection", "TestConnectionAsync resolves the caller identity and caches the account", TestConnection));
            cases.Add(Case("caller-identity", "aws_caller_identity reports account, ARN, region, and allowed services", CallerIdentity));
            cases.Add(Case("identity-resolved-on-first-use", "The first tool call resolves the identity so later approvals name the account", IdentityOnFirstUse));
            cases.Add(Case("assume-role", "assumeRoleArn with externalId assumes the role and signs later calls with the role credentials", AssumeRole));
            cases.Add(Case("rds-instances", "aws_rds_describe instances renders rows and pages with the RDS Marker", RdsInstances));
            cases.Add(Case("rds-clusters", "aws_rds_describe clusters shows writer and reader members", RdsClusters));
            cases.Add(Case("rds-events", "aws_rds_describe events sends the window and source", RdsEvents));
            cases.Add(Case("elb-target-health", "aws_elb_target_health resolves a load balancer and counts unhealthy targets", ElbTargetHealth));
            cases.Add(Case("elb-target-group-arn", "aws_elb_target_health with a target group ARN skips discovery", ElbTargetGroupArn));
            cases.Add(Case("autoscaling-groups", "aws_autoscaling_describe_groups shows capacity, health, and instances", AutoScalingGroups));
            cases.Add(Case("ecs-describe", "aws_ecs_describe_services lists then describes services with events and failures", EcsDescribe));
            cases.Add(Case("ecs-update", "aws_ecs_update_service sends only desired count and force new deployment", EcsUpdate));
            cases.Add(Case("ec2-describe", "aws_ec2_describe_instances renders instances from the EC2 query protocol", Ec2Describe));
            cases.Add(Case("ec2-actions", "aws_ec2_instance_action start, stop, reboot, and terminate call the matching APIs", Ec2Actions));
            cases.Add(Case("error-access-denied", "AccessDenied maps to permission_denied", ErrorAccessDenied));
            cases.Add(Case("error-throttling", "Throttling maps to rate_limited with a retry-after hint", ErrorThrottling));
            cases.Add(Case("error-not-found", "DBInstanceNotFound and ClusterNotFoundException map to not_found", ErrorNotFound));
            cases.Add(Case("error-invalid-parameter", "InvalidParameterValue maps to invalid_argument", ErrorInvalidParameter));
            cases.Add(Case("error-json-protocol", "ECS JSON errors map like query errors", ErrorJsonProtocol));
            cases.Add(Case("error-ec2-unauthorized", "EC2 UnauthorizedOperation maps to permission_denied", ErrorEc2Unauthorized));
            cases.Add(Case("error-auth-invalidates-session", "InvalidClientTokenId maps to auth_failed and drops the pooled session", ErrorAuthInvalidates));
            cases.Add(Case("allowed-services-no-call", "A disallowed service never reaches the endpoint", AllowedServicesNoCall));
            cases.Add(Case("output-row-limit", "Rows beyond the output row limit are cut and flagged", OutputRowLimit));
            cases.Add(Case("timeout", "A slow endpoint returns a timeout error within the call timeout", Timeout));
            cases.Add(Case("cancellation", "A cancelled caller token propagates OperationCanceledException", Cancellation));
            cases.Add(Case("secret-scrubbing", "No output, structured result, or error carries the secret access key", SecretScrubbing));
            return new TestSuiteDescriptor(SuiteId, "AWS connector (recorded-response stub)", cases, null, null);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body, null, false, null);
        }

        private static ConnectorInvocationContext Context(AwsStubServer stub, Dictionary<string, string>? settings = null, TimeSpan? timeout = null)
        {
            return AwsTestSupport.Context(AwsTestSupport.Resource(stub.BaseAddress, settings), timeout);
        }

        private static async Task TestConnection(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorResourceContext resource = AwsTestSupport.Resource(stub.BaseAddress);
                ConnectorHealthResult health = await AwsTestSupport.Connector.TestConnectionAsync(resource, token).ConfigureAwait(false);
                AwsAssert.True(health.Success, "health ok: " + health.Message);
                AwsAssert.Equal(AwsRecordedResponses.Account, health.Details["account"], "account detail");
                AwsAssert.Contains(health.Message, "123456789012", "message names the account");
                AwsAssert.Equal(AwsRecordedResponses.Account, AwsIdentityCache.Get(resource)?.Account, "identity cached");
                AwsAssert.True(health.LatencyMs >= 0, "latency recorded");
            }
        }

        private static async Task CallerIdentity(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub, new Dictionary<string, string> { { "allowedServices", "ec2\nlogs" } });
                ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_caller_identity", "{}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "caller identity");
                AwsAssert.Contains(result.Text, "account: 123456789012", "text account");
                AwsAssert.Contains(result.Text, "arn:aws:iam::123456789012:user/hugo-stub", "text arn");
                AwsAssert.Contains(result.Text, "allowedServices: ec2, logs", "text services");
                AwsAssert.Contains(result.StructuredJson, "\"account\":\"123456789012\"", "structured account");
                AwsAssert.Contains(result.StructuredJson, "\"region\":\"us-east-1\"", "structured region");
            }
        }

        private static async Task IdentityOnFirstUse(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub);
                EffectClassification before = AwsTestSupport.Classify(context, "aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[\"i-0123456789abcdef0\"]}");
                AwsAssert.Contains(before.ApprovalSummary, "account not yet confirmed", "summary before");
                ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "rds describe");
                AwsAssert.Equal(1, stub.Count("GetCallerIdentity"), "identity resolved once");
                AwsAssert.Contains(result.Text, "account 123456789012", "text names the account");
                EffectClassification after = AwsTestSupport.Classify(context, "aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[\"i-0123456789abcdef0\"]}");
                AwsAssert.Contains(after.ApprovalSummary, "account 123456789012", "summary after");
                await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Equal(1, stub.Count("GetCallerIdentity"), "identity not resolved again");
            }
        }

        private static async Task AssumeRole(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub, new Dictionary<string, string>
                {
                    { "assumeRoleArn", "arn:aws:iam::123456789012:role/HugoReadOnly" },
                    { "externalId", "hugo-external-42" },
                    { "sessionName", "hugo-tests" },
                    { "assumeRoleDurationSeconds", "1800" }
                });
                ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "rds via assumed role");
                string body = Uri.UnescapeDataString(stub.LastBody("AssumeRole"));
                AwsAssert.Contains(body, "RoleArn=arn:aws:iam::123456789012:role/HugoReadOnly", "role arn sent");
                AwsAssert.Contains(body, "ExternalId=hugo-external-42", "external id sent");
                AwsAssert.Contains(body, "RoleSessionName=hugo-tests", "session name sent");
                AwsAssert.Contains(body, "DurationSeconds=1800", "duration sent");
                AwsAssert.Contains(stub.LastAuthorization("AssumeRole"), AwsTestSupport.AccessKeyId, "AssumeRole signed with the base key");
                AwsAssert.Contains(stub.LastAuthorization("DescribeDBInstances"), AwsRecordedResponses.AssumedAccessKeyId, "later calls signed with the role key");

                ToolResult identity = await AwsTestSupport.InvokeAsync(context, "aws_caller_identity", "{}", token).ConfigureAwait(false);
                AwsAssert.Contains(identity.Text, "credentialSource: assumed role", "credential source");
                AwsAssert.Contains(identity.Text, "credentialsExpire:", "expiry shown");
            }
        }

        private static async Task RdsInstances(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub);
                ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{\"kind\":\"instances\",\"maxResults\":25}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "rds instances");
                AwsAssert.Contains(result.Text, "| orders-db | available | postgres 16.4 | db.r6g.large | yes | us-east-1a | orders-db.c1example.us-east-1.rds.amazonaws.com:5432 | 200 |", "orders-db row");
                AwsAssert.Contains(result.Text, "inventory-aurora-1", "aurora row");
                AwsAssert.Contains(result.Text, "nextToken = \"stub-rds-marker-2\"", "paging hint");
                AwsAssert.Contains(result.StructuredJson, "\"nextToken\":\"stub-rds-marker-2\"", "structured next token");
                AwsAssert.Contains(stub.LastBody("DescribeDBInstances"), "MaxRecords=25", "page size sent");

                await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{\"nextToken\":\"stub-rds-marker-2\",\"identifier\":\"orders-db\"}", token).ConfigureAwait(false);
                string body = stub.LastBody("DescribeDBInstances");
                AwsAssert.Contains(body, "Marker=stub-rds-marker-2", "marker sent");
                AwsAssert.Contains(body, "DBInstanceIdentifier=orders-db", "identifier sent");
                AwsAssert.Contains(body, "MaxRecords=50", "default page size");

                using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson!))
                {
                    AwsAssert.Equal(2, doc.RootElement.GetProperty("count").GetInt32(), "structured count");
                    AwsAssert.Equal("orders-db", doc.RootElement.GetProperty("items")[0].GetProperty("instance").GetString(), "structured first item");
                }
            }
        }

        private static async Task RdsClusters(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_rds_describe", "{\"kind\":\"clusters\"}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "rds clusters");
                AwsAssert.Contains(result.Text, "| inventory-aurora | available | aurora-mysql 8.0.mysql_aurora.3.05.2 | yes | inventory-aurora-1 | inventory-aurora-2 |", "cluster row");
            }
        }

        private static async Task RdsEvents(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_rds_describe", "{\"kind\":\"events\",\"identifier\":\"orders-db\",\"lookbackMinutes\":120}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "rds events");
                AwsAssert.Contains(result.Text, "Multi-AZ instance failover completed.", "event message");
                AwsAssert.True(result.Text.IndexOf("failover completed", StringComparison.Ordinal) < result.Text.IndexOf("failover started", StringComparison.Ordinal), "newest first");
                string body = Uri.UnescapeDataString(stub.LastBody("DescribeEvents"));
                AwsAssert.Contains(body, "SourceIdentifier=orders-db", "source sent");
                AwsAssert.Contains(body, "SourceType=db-instance", "source type sent");
                AwsAssert.Contains(body, "StartTime=", "start sent");
                AwsAssert.Contains(body, "EndTime=", "end sent");

                ToolResult tooLong = await AwsTestSupport.InvokeAsync(Context(stub), "aws_rds_describe", "{\"kind\":\"events\",\"lookbackMinutes\":100000}", token).ConfigureAwait(false);
                AwsAssert.Error(tooLong, ConnectorErrorCodes.TimeRangeExceeded, "events window too long");
            }
        }

        private static async Task ElbTargetHealth(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_elb_target_health", "{\"loadBalancerName\":\"web-alb\"}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "target health");
                AwsAssert.Contains(result.Text, "| web-tg | i-0fedcba9876543210 | 8080 | us-east-1b | unhealthy | Target.ResponseCodeMismatch |", "unhealthy row");
                AwsAssert.Contains(result.StructuredJson, "\"notHealthy\":\"1\"", "unhealthy count");
                AwsAssert.Contains(Uri.UnescapeDataString(stub.LastBody("DescribeTargetGroups")), "LoadBalancerArn=arn:aws:elasticloadbalancing:us-east-1:123456789012:loadbalancer/app/web-alb/50dc6c495c0c9188", "lb arn used");
                AwsAssert.Equal(1, stub.Count("DescribeLoadBalancers"), "load balancer resolved");
            }
        }

        private static async Task ElbTargetGroupArn(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                string arn = "arn:aws:elasticloadbalancing:us-east-1:123456789012:targetgroup/web-tg/73e2d6bc24d8a067";
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_elb_target_health", "{\"targetGroupArn\":\"" + arn + "\"}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "target health by arn");
                AwsAssert.Equal(0, stub.Count("DescribeTargetGroups"), "no discovery");
                AwsAssert.Contains(result.Text, "| web-tg | i-0123456789abcdef0 | 8080 | us-east-1a | healthy |", "healthy row");
            }
        }

        private static async Task AutoScalingGroups(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_autoscaling_describe_groups", "{\"groupNames\":[\"web-asg\"],\"includeInstances\":true}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "asg");
                AwsAssert.Contains(result.Text, "| web-asg | 3 | 2 | 6 | 2/3 | 1 | web-template | us-east-1a, us-east-1b |", "group row");
                AwsAssert.Contains(result.Text, "web-asg i-0fedcba9876543210 InService Unhealthy us-east-1b", "instance line");
                AwsAssert.Contains(Uri.UnescapeDataString(stub.LastBody("DescribeAutoScalingGroups")), "AutoScalingGroupNames.member.1=web-asg", "group name sent");
            }
        }

        private static async Task EcsDescribe(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ecs_describe_services", "{\"cluster\":\"shop\",\"eventsPerService\":1}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "ecs describe");
                AwsAssert.Contains(stub.LastBody("ListServices"), "\"cluster\":\"shop\"", "list cluster");
                AwsAssert.Contains(stub.LastBody("DescribeServices"), "service/shop/checkout", "describe uses listed arns");
                AwsAssert.Contains(result.Text, "| checkout | ACTIVE | 4 | 3 | 1 | FARGATE | checkout:42 | PRIMARY/IN_PROGRESS 3/4 checkout:42 |", "checkout row");
                AwsAssert.Contains(result.Text, "failed container health checks", "newest event");
                AwsAssert.NotContains(result.Text, "has started 1 tasks", "older event cut by eventsPerService");
                AwsAssert.Contains(result.Text, "Not described: arn:aws:ecs:us-east-1:123456789012:service/shop/ghost (MISSING)", "failure note");
                AwsAssert.Contains(result.Text, "nextToken = \"stub-ecs-next\"", "paging");
            }
        }

        private static async Task EcsUpdate(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ecs_update_service", "{\"cluster\":\"shop\",\"service\":\"checkout\",\"desiredCount\":6,\"forceNewDeployment\":true}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "ecs update");
                string body = stub.LastBody("UpdateService");
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    AwsAssert.Equal("shop", root.GetProperty("cluster").GetString(), "cluster");
                    AwsAssert.Equal("checkout", root.GetProperty("service").GetString(), "service");
                    AwsAssert.Equal(6, root.GetProperty("desiredCount").GetInt32(), "desired count");
                    AwsAssert.True(root.GetProperty("forceNewDeployment").GetBoolean(), "force new deployment");
                    AwsAssert.Equal(4, root.EnumerateObject().Count(), "no other fields are sent");
                }
                AwsAssert.Contains(result.Text, "| checkout | ACTIVE | 6 | 3 | 0 |", "updated row");

                ToolResult nothing = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ecs_update_service", "{\"service\":\"checkout\"}", token).ConfigureAwait(false);
                AwsAssert.Error(nothing, ConnectorErrorCodes.InvalidArgument, "update without changes");
            }
        }

        private static async Task Ec2Describe(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ec2_describe_instances", "{\"states\":[\"running\"],\"filters\":[{\"name\":\"tag:Name\",\"values\":[\"web-1\"]}]}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "ec2 describe");
                AwsAssert.Contains(result.Text, "| i-0123456789abcdef0 | web-1 | running | t3.micro | us-east-1a | 10.0.1.15 | NULL | 2026-10-01T08:00:00Z |", "instance row");
                string body = Uri.UnescapeDataString(stub.LastBody("DescribeInstances"));
                AwsAssert.Contains(body, "Filter.1.Name=tag:Name", "tag filter");
                AwsAssert.Contains(body, "Filter.2.Name=instance-state-name", "state filter");
                AwsAssert.Contains(body, "MaxResults=50", "page size");

                await AwsTestSupport.InvokeAsync(Context(stub), "aws_ec2_describe_instances", "{\"instanceIds\":[\"i-0123456789abcdef0\"]}", token).ConfigureAwait(false);
                string byId = Uri.UnescapeDataString(stub.LastBody("DescribeInstances"));
                AwsAssert.Contains(byId, "InstanceId.1=i-0123456789abcdef0", "id sent");
                AwsAssert.NotContains(byId, "MaxResults", "no page size with ids");
            }
        }

        private static async Task Ec2Actions(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub);
                string id = AwsRecordedResponses.InstanceId;
                ToolResult start = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"start\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
                AwsAssert.Ok(start, "start");
                AwsAssert.Contains(start.Text, "| " + id + " | stopped | pending |", "start transition");
                ToolResult stop = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"stop\",\"force\":true,\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
                AwsAssert.Ok(stop, "stop");
                AwsAssert.Contains(stop.Text, "| " + id + " | running | stopping |", "stop transition");
                AwsAssert.Contains(stub.LastBody("StopInstances"), "Force=true", "force sent");
                ToolResult reboot = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"reboot\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
                AwsAssert.Ok(reboot, "reboot");
                AwsAssert.Equal(1, stub.Count("RebootInstances"), "reboot called");
                ToolResult terminate = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"terminate\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
                AwsAssert.Ok(terminate, "terminate");
                AwsAssert.Contains(terminate.Text, "| " + id + " | running | shutting-down |", "terminate transition");
                ToolResult bad = await AwsTestSupport.InvokeAsync(context, "aws_ec2_instance_action", "{\"action\":\"hibernate\",\"instanceIds\":[\"" + id + "\"]}", token).ConfigureAwait(false);
                AwsAssert.Error(bad, ConnectorErrorCodes.InvalidArgument, "unknown action");
            }
        }

        private static async Task ErrorAccessDenied(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.FailNext("DescribeDBInstances", AwsStubResponse.QueryError(403, "AccessDenied", "User: arn:aws:iam::123456789012:user/hugo-stub is not authorized to perform: rds:DescribeDBInstances"));
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Error(result, ConnectorErrorCodes.PermissionDenied, "access denied");
                AwsAssert.Contains(result.Text, "rds:DescribeDBInstances", "message names the action");
                AwsAssert.Contains(result.Text, "aws-iam-policies", "message points to the policy guide");
            }
        }

        private static async Task ErrorThrottling(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.FailNext("DescribeTargetHealth", AwsStubResponse.QueryError(400, "Throttling", "Rate exceeded"));
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_elb_target_health", "{\"targetGroupArn\":\"arn:aws:elasticloadbalancing:us-east-1:123456789012:targetgroup/web-tg/1\"}", token).ConfigureAwait(false);
                AwsAssert.Error(result, ConnectorErrorCodes.RateLimited, "throttling");
                AwsAssert.True(result.RetryAfterSeconds != null && result.RetryAfterSeconds.Value >= 1, "retry-after set");

                stub.FailNext("DescribeTargetHealth", AwsStubResponse.QueryError(400, "Throttling", "Rate exceeded"), 1);
                ToolResult retried = await AwsTestSupport.InvokeAsync(Context(stub, new Dictionary<string, string> { { "maxRetries", "2" } }), "aws_elb_target_health", "{\"targetGroupArn\":\"arn:aws:elasticloadbalancing:us-east-1:123456789012:targetgroup/web-tg/1\"}", token).ConfigureAwait(false);
                AwsAssert.Ok(retried, "SDK retry absorbs one throttle when maxRetries > 0");
            }
        }

        private static async Task ErrorNotFound(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.FailNext("DescribeDBInstances", AwsStubResponse.QueryError(404, "DBInstanceNotFound", "DBInstance ghost-db not found."));
                ToolResult rds = await AwsTestSupport.InvokeAsync(Context(stub), "aws_rds_describe", "{\"identifier\":\"ghost-db\"}", token).ConfigureAwait(false);
                AwsAssert.Error(rds, ConnectorErrorCodes.NotFound, "rds not found");
                AwsAssert.Contains(rds.Text, "ghost-db", "message names the instance");

                stub.FailNext("ListServices", AwsStubResponse.JsonError(400, "ClusterNotFoundException", "Cluster not found."));
                ToolResult ecs = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ecs_describe_services", "{\"cluster\":\"ghost\"}", token).ConfigureAwait(false);
                AwsAssert.Error(ecs, ConnectorErrorCodes.NotFound, "ecs cluster not found");
            }
        }

        private static async Task ErrorInvalidParameter(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.FailNext("DescribeAutoScalingGroups", AwsStubResponse.QueryError(400, "ValidationError", "1 validation error detected: Value 'x' at 'maxRecords'"));
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_autoscaling_describe_groups", "{}", token).ConfigureAwait(false);
                AwsAssert.Error(result, ConnectorErrorCodes.InvalidArgument, "validation error");
                stub.FailNext("DescribeDBClusters", AwsStubResponse.QueryError(400, "InvalidParameterValue", "Invalid cluster identifier"));
                ToolResult rds = await AwsTestSupport.InvokeAsync(Context(stub), "aws_rds_describe", "{\"kind\":\"clusters\",\"identifier\":\"!bad\"}", token).ConfigureAwait(false);
                AwsAssert.Error(rds, ConnectorErrorCodes.InvalidArgument, "invalid parameter value");
            }
        }

        private static async Task ErrorJsonProtocol(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.FailNext("UpdateService", AwsStubResponse.JsonError(400, "AccessDeniedException", "User is not authorized to perform ecs:UpdateService"));
                ToolResult denied = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ecs_update_service", "{\"service\":\"checkout\",\"desiredCount\":2}", token).ConfigureAwait(false);
                AwsAssert.Error(denied, ConnectorErrorCodes.PermissionDenied, "ecs access denied");
                stub.FailNext("DescribeServices", AwsStubResponse.JsonError(400, "ThrottlingException", "Rate exceeded"));
                ToolResult throttled = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ecs_describe_services", "{\"services\":[\"checkout\"]}", token).ConfigureAwait(false);
                AwsAssert.Error(throttled, ConnectorErrorCodes.RateLimited, "ecs throttled");
                AwsAssert.True(throttled.RetryAfterSeconds != null, "ecs retry-after");
            }
        }

        private static async Task ErrorEc2Unauthorized(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.FailNext("TerminateInstances", AwsStubResponse.Ec2Error(403, "UnauthorizedOperation", "You are not authorized to perform this operation."));
                ToolResult result = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ec2_instance_action", "{\"action\":\"terminate\",\"instanceIds\":[\"i-0123456789abcdef0\"]}", token).ConfigureAwait(false);
                AwsAssert.Error(result, ConnectorErrorCodes.PermissionDenied, "ec2 unauthorized");
                stub.FailNext("DescribeInstances", AwsStubResponse.Ec2Error(400, "InvalidInstanceID.NotFound", "The instance ID 'i-0aaaabbbbccccdddd' does not exist"));
                ToolResult missing = await AwsTestSupport.InvokeAsync(Context(stub), "aws_ec2_describe_instances", "{\"instanceIds\":[\"i-0aaaabbbbccccdddd\"]}", token).ConfigureAwait(false);
                AwsAssert.Error(missing, ConnectorErrorCodes.NotFound, "ec2 not found");
            }
        }

        private static async Task ErrorAuthInvalidates(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub);
                CountingPool pool = new CountingPool();
                context.Resource.Sessions = pool;
                ToolResult ok = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Ok(ok, "first call");
                stub.FailNext("DescribeDBInstances", AwsStubResponse.QueryError(403, "InvalidClientTokenId", "The security token included in the request is invalid."));
                ToolResult failed = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Error(failed, ConnectorErrorCodes.AuthFailed, "invalid token");
                AwsAssert.Equal(1, pool.Invalidations, "session invalidated");
                AwsAssert.Equal(null, AwsIdentityCache.Get(context.Resource), "identity forgotten");
                ToolResult again = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Ok(again, "recovers with a new session");
                AwsAssert.Equal(2, pool.Created, "a new session was built");
            }
        }

        private static async Task AllowedServicesNoCall(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub, new Dictionary<string, string> { { "allowedServices", "ec2" } });
                ToolResult rds = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Error(rds, ConnectorErrorCodes.PermissionDenied, "rds refused");
                AwsAssert.Equal(0, stub.Actions.Count, "stub received nothing");
                ToolResult ec2 = await AwsTestSupport.InvokeAsync(context, "aws_ec2_describe_instances", "{}", token).ConfigureAwait(false);
                AwsAssert.Ok(ec2, "ec2 allowed");
            }
        }

        private static async Task OutputRowLimit(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub);
                context.Limits = new OutputLimits { MaxRows = 1 };
                ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Ok(result, "rds limited");
                AwsAssert.True(result.Truncated, "truncated flag");
                AwsAssert.Contains(result.Text, "truncated to the output limit", "truncation note");
                AwsAssert.NotContains(result.Text, "inventory-aurora-1", "second row cut");
                AwsAssert.Contains(result.StructuredJson, "\"truncated\":true", "structured truncated");

                ConnectorInvocationContext small = Context(stub);
                small.Limits = new OutputLimits { MaxBytes = 1024 };
                ToolResult bytes = await AwsTestSupport.InvokeAsync(small, "aws_ecs_describe_services", "{\"services\":[\"checkout\",\"catalog\"]}", token).ConfigureAwait(false);
                AwsAssert.Ok(bytes, "ecs small");
                AwsAssert.True(System.Text.Encoding.UTF8.GetByteCount(bytes.Text) <= 1024 + 80, "text bounded by bytes");
            }
        }

        private static async Task Timeout(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.DelayMilliseconds = 4000;
                ConnectorInvocationContext context = Context(stub, null, TimeSpan.FromSeconds(1));
                DateTime started = DateTime.UtcNow;
                ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Error(result, ConnectorErrorCodes.Timeout, "slow endpoint");
                AwsAssert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3.5), "returned near the timeout");
            }
        }

        private static async Task Cancellation(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                stub.DelayMilliseconds = 4000;
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(300);
                    bool cancelled = false;
                    try
                    {
                        await AwsTestSupport.InvokeAsync(Context(stub), "aws_rds_describe", "{}", cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    AwsAssert.True(cancelled, "OperationCanceledException propagated");
                }
            }
        }

        private static async Task SecretScrubbing(CancellationToken token)
        {
            using (AwsStubServer stub = new AwsStubServer())
            {
                ConnectorInvocationContext context = Context(stub);
                foreach (string tool in new string[] { "aws_caller_identity", "aws_rds_describe", "aws_ecs_describe_services", "aws_elb_target_health", "aws_autoscaling_describe_groups" })
                {
                    ToolResult result = await AwsTestSupport.InvokeAsync(context, tool, "{}", token).ConfigureAwait(false);
                    AwsTestSupport.AssertNoSecret(result, tool);
                }
                stub.FailNext("DescribeDBInstances", AwsStubResponse.QueryError(403, "SignatureDoesNotMatch", "The request signature we calculated does not match the signature you provided."));
                ToolResult error = await AwsTestSupport.InvokeAsync(context, "aws_rds_describe", "{}", token).ConfigureAwait(false);
                AwsAssert.Error(error, ConnectorErrorCodes.AuthFailed, "signature mismatch");
                AwsTestSupport.AssertNoSecret(error, "auth error");
                ConnectorHealthResult health = await AwsTestSupport.Connector.TestConnectionAsync(context.Resource, token).ConfigureAwait(false);
                AwsAssert.NotContains(health.Message, AwsTestSupport.SecretAccessKey, "health message");
                foreach (KeyValuePair<string, string> detail in health.Details) AwsAssert.NotContains(detail.Value, AwsTestSupport.SecretAccessKey, "health detail " + detail.Key);
            }
        }

        #endregion
    }
}
