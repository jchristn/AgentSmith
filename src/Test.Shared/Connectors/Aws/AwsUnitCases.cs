namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws;
    using Hugo.Connectors.Aws.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Offline unit cases for the AWS connector: descriptor, schemas, settings validation, options, the operation
    /// classifier, per-call classification of every tool and action, allowedServices enforcement, error mapping, time
    /// windows, and argument helpers. No network access.
    /// </summary>
    public static class AwsUnitCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "connectors.aws";

        #endregion

        #region Private-Members

        private static readonly string[] _ToolNames = new string[]
        {
            "aws_caller_identity", "aws_ec2_describe_instances", "aws_ec2_instance_action", "aws_autoscaling_describe_groups",
            "aws_cloudwatch_get_metric_data", "aws_cloudwatch_list_alarms", "aws_logs_insights_query", "aws_logs_tail",
            "aws_ecs_describe_services", "aws_ecs_update_service", "aws_rds_describe", "aws_lambda_list_functions",
            "aws_lambda_invoke", "aws_elb_target_health", "aws_route53_list_records", "aws_sqs_queue_attributes", "aws_sns_list_topics"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("descriptor", "Descriptor declares type, family, category, icon, settings, slot, and instructions", Descriptor));
            cases.Add(Case("tool-set", "Every planned tool exists once with the aws_ prefix", ToolSet));
            cases.Add(Case("schemas", "Every input schema is a JSON object schema whose required properties exist", Schemas));
            cases.Add(Case("base-effects", "Each fixed-effect tool's base effect matches its operations under the 8.3 rule", BaseEffects));
            cases.Add(Case("operation-classifier", "Operation prefix classifier table (8.3), unknown is Destructive", OperationClassifier));
            cases.Add(Case("classify-table", "Classification of every tool, action, and edge case", ClassifyTable));
            cases.Add(Case("terminate-always-approves", "Terminate is Destructive and always requires approval; stop and reboot do not", TerminateAlwaysApproves));
            cases.Add(Case("approval-summary-account", "Approval summaries name the cached account, region, binding, and resource IDs", ApprovalSummaryAccount));
            cases.Add(Case("approval-summary-unconfirmed", "Without a cached identity the summary says the account is not yet confirmed", ApprovalSummaryUnconfirmed));
            cases.Add(Case("allowed-services-classify", "Tools outside allowedServices are rejected at classification; STS stays allowed", AllowedServicesClassify));
            cases.Add(Case("allowed-services-execute", "Tools outside allowedServices fail with permission_denied without calling AWS", AllowedServicesExecute));
            cases.Add(Case("allowed-services-parsing", "allowedServices accepts lines and commas, any case", AllowedServicesParsing));
            cases.Add(Case("validate-ok", "A key pair resource and an instance role resource validate", ValidateOk));
            cases.Add(Case("validate-credentials", "Missing, partial, and conflicting credentials fail validation", ValidateCredentials));
            cases.Add(Case("validate-fields", "Region, role ARN, external ID, session name, services, endpoint, and ranges are checked", ValidateFields));
            cases.Add(Case("options-defaults", "Options apply defaults and clamp values", OptionsDefaults));
            cases.Add(Case("error-mapping", "SDK exceptions map to stable codes with retry-after on throttling", ErrorMapping));
            cases.Add(Case("error-code-table", "Error code and status table", ErrorCodeTable));
            cases.Add(Case("time-window", "Time windows: lookback, explicit, inverted, too long, malformed", TimeWindow));
            cases.Add(Case("metric-auto-period", "Automatic metric period targets about 120 points in whole minutes", MetricAutoPeriod));
            cases.Add(Case("arguments", "Instance IDs, filters, Lambda payloads, and Route 53 tokens parse strictly", Arguments));
            cases.Add(Case("endpoint-parse", "Endpoint override must be an absolute http(s) URL without credentials", EndpointParse));
            cases.Add(Case("execute-secret-missing", "A resource without credentials fails with secret_missing", ExecuteSecretMissing));
            cases.Add(Case("test-connection-invalid", "TestConnectionAsync reports invalid settings without connecting", TestConnectionInvalid));
            cases.Add(Case("test-connection-unreachable", "TestConnectionAsync reports connection_failed for a closed port", TestConnectionUnreachable));
            return new TestSuiteDescriptor(SuiteId, "AWS connector (offline)", cases, null, null);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body, null, false, null);
        }

        private static Task Descriptor(CancellationToken token)
        {
            ConnectorDescriptor d = AwsTestSupport.Connector.Descriptor;
            AwsAssert.Equal("aws", d.Type, "type");
            AwsAssert.Equal("aws", d.Family, "family");
            AwsAssert.Equal(ConnectorCategoryEnum.Cloud, d.Category, "category");
            AwsAssert.Equal("cloud", d.IconKey, "icon");
            AwsAssert.Equal("connector.aws.instructions", d.DefaultInstructionsKey, "instructions key");
            AwsAssert.Contains(d.DefaultInstructions, "aws_caller_identity", "instructions");
            AwsAssert.True(!d.DefaultInstructions.Contains((char)0x2014), "instructions contain no em-dash");
            foreach (string key in new string[] { "region", "assumeRoleArn", "externalId", "sessionName", "useInstanceRole", "allowedServices", "endpointOverride", "maxResults" })
                AwsAssert.True(d.SettingsSchema.Find(key) != null, "setting " + key + " declared");
            AwsAssert.Equal(SettingValueKindEnum.StringList, d.SettingsSchema.Find("allowedServices")!.Kind, "allowedServices kind");
            AwsAssert.Equal(SettingValueKindEnum.Boolean, d.SettingsSchema.Find("useInstanceRole")!.Kind, "useInstanceRole kind");
            AwsAssert.Equal("us-east-1", d.SettingsSchema.Find("region")!.DefaultValue, "region default");
            AwsAssert.Equal(1, d.SecretSlots.Count, "slot count");
            AwsAssert.Equal("accessKeyPair", d.SecretSlots[0].Name, "slot name");
            AwsAssert.True(d.SecretSlots[0].AcceptedKinds.SequenceEqual(new SecretKindEnum[] { SecretKindEnum.AccessKeyPair }), "slot accepts AccessKeyPair");
            return Task.CompletedTask;
        }

        private static Task ToolSet(CancellationToken token)
        {
            List<string> names = AwsTestSupport.Connector.Tools.Select(t => t.Descriptor.Name).ToList();
            AwsAssert.Equal(_ToolNames.Length, names.Count, "tool count");
            foreach (string expected in _ToolNames) AwsAssert.True(names.Contains(expected), "tool " + expected + " exists");
            AwsAssert.Equal(names.Count, names.Distinct().Count(), "unique tool names");
            foreach (IConnectorTool tool in AwsTestSupport.Connector.Tools)
            {
                AwsAssert.True(tool.Descriptor.Name.StartsWith("aws_", StringComparison.Ordinal), tool.Descriptor.Name + " prefix");
                AwsAssert.True(tool.Descriptor.Description.Length > 40, tool.Descriptor.Name + " has a description");
                AwsAssert.True(tool is AwsToolBase, tool.Descriptor.Name + " derives from AwsToolBase");
                AwsAssert.True(AwsServiceNames.IsKnown(((AwsToolBase)tool).Service), tool.Descriptor.Name + " service is known");
                AwsAssert.True(!tool.Descriptor.Description.Contains((char)0x2014), tool.Descriptor.Name + " description has no em-dash");
            }
            return Task.CompletedTask;
        }

        private static Task Schemas(CancellationToken token)
        {
            foreach (IConnectorTool tool in AwsTestSupport.Connector.Tools)
            {
                using (JsonDocument doc = JsonDocument.Parse(tool.Descriptor.InputSchemaJson))
                {
                    JsonElement root = doc.RootElement;
                    AwsAssert.Equal("object", root.GetProperty("type").GetString(), tool.Descriptor.Name + " schema type");
                    JsonElement props = root.GetProperty("properties");
                    AwsAssert.True(!props.TryGetProperty("resource", out JsonElement _), tool.Descriptor.Name + " does not declare resource");
                    if (root.TryGetProperty("required", out JsonElement required))
                        foreach (JsonElement r in required.EnumerateArray())
                            AwsAssert.True(props.TryGetProperty(r.GetString()!, out JsonElement _), tool.Descriptor.Name + " required property " + r.GetString() + " is declared");
                    AssertDescribed(tool.Descriptor.Name, props);
                }
            }

            using (JsonDocument insights = JsonDocument.Parse(AwsTestSupport.Tool("aws_logs_insights_query").Descriptor.InputSchemaJson))
            {
                List<string> required = insights.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString() ?? String.Empty).ToList();
                AwsAssert.True(required.Contains("logGroupNames") && required.Contains("query"), "aws_logs_insights_query requires logGroupNames and query");
            }
            return Task.CompletedTask;
        }

        private static void AssertDescribed(string tool, JsonElement properties)
        {
            foreach (JsonProperty p in properties.EnumerateObject())
            {
                bool described = p.Value.TryGetProperty("description", out JsonElement d) && !String.IsNullOrWhiteSpace(d.GetString());
                AwsAssert.True(described, tool + " parameter " + p.Name + " has a description");
                if (p.Value.TryGetProperty("properties", out JsonElement nested)) AssertDescribed(tool, nested);
                if (p.Value.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Object && items.TryGetProperty("properties", out JsonElement itemProps))
                    AssertDescribed(tool, itemProps);
            }
        }

        private static Task BaseEffects(CancellationToken token)
        {
            foreach (IConnectorTool tool in AwsTestSupport.Connector.Tools)
            {
                AwsToolBase aws = (AwsToolBase)tool;
                AwsAssert.True(aws.Operations.Count > 0, aws.Descriptor.Name + " declares operations");
                if (aws.Descriptor.ClassifiedPerCall) continue;
                EffectEnum derived = AwsOperationClassifier.ClassifyAll(aws.Operations);
                AwsAssert.Equal(derived, aws.Descriptor.BaseEffect, aws.Descriptor.Name + " base effect vs operations " + String.Join(",", aws.Operations));
            }
            AwsAssert.Equal(EffectEnum.Destructive, AwsTestSupport.Tool("aws_ec2_instance_action").Descriptor.BaseEffect, "instance action base effect");
            AwsAssert.True(AwsTestSupport.Tool("aws_ec2_instance_action").Descriptor.ClassifiedPerCall, "instance action is classified per call");
            AwsAssert.Equal(EffectEnum.Write, AwsTestSupport.Tool("aws_ecs_update_service").Descriptor.BaseEffect, "ecs update base effect");
            AwsAssert.Equal(EffectEnum.Write, AwsTestSupport.Tool("aws_lambda_invoke").Descriptor.BaseEffect, "lambda invoke base effect");
            return Task.CompletedTask;
        }

        private static Task OperationClassifier(CancellationToken token)
        {
            Dictionary<string, EffectEnum> table = new Dictionary<string, EffectEnum>(StringComparer.Ordinal)
            {
                { "ec2:DescribeInstances", EffectEnum.Read },
                { "DescribeInstances", EffectEnum.Read },
                { "ecs:ListServices", EffectEnum.Read },
                { "sts:GetCallerIdentity", EffectEnum.Read },
                { "logs:GetQueryResults", EffectEnum.Read },
                { "route53:ListResourceRecordSets", EffectEnum.Read },
                { "logs:StartQuery", EffectEnum.Read },
                { "logs:StopQuery", EffectEnum.Read },
                { "logs:FilterLogEvents", EffectEnum.Read },
                { "ec2:StartInstances", EffectEnum.Write },
                { "ecs:UpdateService", EffectEnum.Write },
                { "cloudwatch:PutMetricAlarm", EffectEnum.Write },
                { "lambda:CreateFunction", EffectEnum.Write },
                { "rds:ModifyDBInstance", EffectEnum.Write },
                { "lambda:InvokeFunction", EffectEnum.Write },
                { "InvokeFunction", EffectEnum.Write },
                { "ec2:StopInstances", EffectEnum.Destructive },
                { "ec2:TerminateInstances", EffectEnum.Destructive },
                { "ec2:RebootInstances", EffectEnum.Destructive },
                { "rds:RebootDBInstance", EffectEnum.Destructive },
                { "sqs:DeleteQueue", EffectEnum.Destructive },
                { "rds:DeleteDBCluster", EffectEnum.Destructive },
                { "ecs:StopTask", EffectEnum.Destructive },
                { "sqs:PurgeQueue", EffectEnum.Destructive },
                { "ec2:RunInstances", EffectEnum.Destructive },
                { "iam:AttachRolePolicy", EffectEnum.Destructive },
                { "sns:Publish", EffectEnum.Destructive },
                { "Gettysburg", EffectEnum.Destructive },
                { "Listen", EffectEnum.Destructive },
                { "Startup", EffectEnum.Destructive },
                { "", EffectEnum.Destructive },
                { "ec2:", EffectEnum.Destructive },
                { "describeInstances", EffectEnum.Destructive }
            };
            foreach (KeyValuePair<string, EffectEnum> row in table)
                AwsAssert.Equal(row.Value, AwsOperationClassifier.Classify(row.Key), "classify '" + row.Key + "'");
            AwsAssert.Equal(EffectEnum.Destructive, AwsOperationClassifier.Classify(null), "null operation");
            AwsAssert.Equal(EffectEnum.Destructive, AwsOperationClassifier.ClassifyAll(new string[] { "ec2:DescribeInstances", "ec2:TerminateInstances" }), "most severe wins");
            AwsAssert.Equal(EffectEnum.Write, AwsOperationClassifier.ClassifyAll(new string[] { "ec2:DescribeInstances", "ec2:StartInstances" }), "write over read");
            AwsAssert.Equal(EffectEnum.Destructive, AwsOperationClassifier.ClassifyAll(new string[0]), "empty set fails closed");
            return Task.CompletedTask;
        }

        private static List<AwsClassifierCase> ClassifierCases()
        {
            List<AwsClassifierCase> c = new List<AwsClassifierCase>();
            c.Add(new AwsClassifierCase("aws_caller_identity", "{}", EffectEnum.Read, "region us-east-1"));
            c.Add(new AwsClassifierCase("aws_ec2_describe_instances", "{}", EffectEnum.Read, "aws-prod"));
            c.Add(new AwsClassifierCase("aws_ec2_describe_instances", "{\"states\":[\"running\"],\"filters\":[{\"name\":\"tag:env\",\"values\":[\"prod\"]}]}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_autoscaling_describe_groups", "{}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_cloudwatch_get_metric_data", "{\"metrics\":[{\"namespace\":\"AWS/EC2\",\"metricName\":\"CPUUtilization\"}]}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_cloudwatch_list_alarms", "{\"state\":\"ALARM\"}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_logs_insights_query", "{\"logGroupNames\":[\"/app\"],\"query\":\"fields @message\"}", EffectEnum.Read, "fields @message"));
            c.Add(new AwsClassifierCase("aws_logs_insights_query", "{\"queryId\":\"q-1\"}", EffectEnum.Read, "q-1"));
            c.Add(new AwsClassifierCase("aws_logs_tail", "{\"logGroupName\":\"/app\"}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_ecs_describe_services", "{\"cluster\":\"shop\"}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_rds_describe", "{\"kind\":\"events\"}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_lambda_list_functions", "{}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_elb_target_health", "{\"loadBalancerName\":\"web-alb\"}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_route53_list_records", "{\"zoneName\":\"example.com\"}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_sqs_queue_attributes", "{\"queueName\":\"orders\"}", EffectEnum.Read));
            c.Add(new AwsClassifierCase("aws_sns_list_topics", "{}", EffectEnum.Read));

            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"action\":\"start\",\"instanceIds\":[\"i-0123456789abcdef0\"]}", EffectEnum.Write, "Start EC2 instance i-0123456789abcdef0"));
            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[\"i-0123456789abcdef0\"]}", EffectEnum.Destructive, "Stop EC2 instance i-0123456789abcdef0"));
            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"action\":\"stop\",\"force\":true,\"instanceIds\":[\"i-0123456789abcdef0\"]}", EffectEnum.Destructive, "Force-stop"));
            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"action\":\"reboot\",\"instanceIds\":[\"i-0123456789abcdef0\",\"i-0fedcba9876543210\"]}", EffectEnum.Destructive, "i-0123456789abcdef0, i-0fedcba9876543210"));
            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"action\":\"terminate\",\"instanceIds\":[\"i-0123456789abcdef0\"]}", EffectEnum.Destructive, "Terminate EC2 instance i-0123456789abcdef0") { AlwaysRequireApproval = true });
            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"action\":\"TERMINATE\",\"instanceIds\":[\"i-01234567\"]}", EffectEnum.Destructive) { AlwaysRequireApproval = true });
            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"action\":\"hibernate\",\"instanceIds\":[\"i-0123456789abcdef0\"]}", EffectEnum.Destructive, "Unknown EC2 action"));
            c.Add(new AwsClassifierCase("aws_ec2_instance_action", "{\"instanceIds\":[\"i-0123456789abcdef0\"]}", EffectEnum.Destructive));
            c.Add(AwsClassifierCase.Reject("aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[]}"));
            c.Add(AwsClassifierCase.Reject("aws_ec2_instance_action", "{\"action\":\"stop\"}"));
            c.Add(AwsClassifierCase.Reject("aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[\"web-1\"]}"));
            c.Add(AwsClassifierCase.Reject("aws_ec2_instance_action", "{\"action\":\"terminate\",\"instanceIds\":[\"i-*\"]}"));
            c.Add(AwsClassifierCase.Reject("aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[" + String.Join(",", Enumerable.Range(0, 21).Select(i => "\"i-" + i.ToString("x8") + "\"")) + "]}"));

            c.Add(new AwsClassifierCase("aws_ecs_update_service", "{\"cluster\":\"shop\",\"service\":\"checkout\",\"desiredCount\":4}", EffectEnum.Write, "desired count 4"));
            c.Add(new AwsClassifierCase("aws_ecs_update_service", "{\"service\":\"checkout\",\"forceNewDeployment\":true}", EffectEnum.Write, "force a new deployment"));
            c.Add(new AwsClassifierCase("aws_ecs_update_service", "{\"service\":\"checkout\",\"desiredCount\":0}", EffectEnum.Destructive, "desired count 0"));
            c.Add(AwsClassifierCase.Reject("aws_ecs_update_service", "{\"service\":\"checkout\"}"));
            c.Add(AwsClassifierCase.Reject("aws_ecs_update_service", "{\"desiredCount\":2}"));
            c.Add(AwsClassifierCase.Reject("aws_ecs_update_service", "{\"service\":\"checkout\",\"desiredCount\":-1}"));

            c.Add(new AwsClassifierCase("aws_lambda_invoke", "{\"functionName\":\"billing-report\",\"payload\":{\"day\":\"2026-10-14\"}}", EffectEnum.Write, "billing-report"));
            c.Add(new AwsClassifierCase("aws_lambda_invoke", "{\"functionName\":\"billing-report\",\"payload\":\"{\\\"a\\\":1}\",\"invocationType\":\"Event\"}", EffectEnum.Write, "asynchronously"));
            c.Add(new AwsClassifierCase("aws_lambda_invoke", "{\"functionName\":\"f\",\"qualifier\":\"live\"}", EffectEnum.Write, "f:live"));
            c.Add(AwsClassifierCase.Reject("aws_lambda_invoke", "{\"functionName\":\"f\",\"payload\":\"not json\"}"));
            c.Add(AwsClassifierCase.Reject("aws_lambda_invoke", "{\"payload\":{}}"));
            c.Add(AwsClassifierCase.Reject("aws_lambda_invoke", "{\"functionName\":\"f\",\"payload\":{\"blob\":\"" + new string('x', 70000) + "\"}}"));
            return c;
        }

        private static Task ClassifyTable(CancellationToken token)
        {
            ConnectorInvocationContext context = AwsTestSupport.Context(AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint));
            List<AwsClassifierCase> cases = ClassifierCases();
            AwsAssert.True(cases.Count >= 40, "at least 40 classification cases");
            foreach (AwsClassifierCase row in cases)
            {
                EffectClassification c = AwsTestSupport.Classify(context, row.Tool, row.Arguments);
                if (row.Rejected)
                {
                    AwsAssert.True(c.Rejected, row + " is rejected (got " + c.Effect + ": " + c.Reason + ")");
                    continue;
                }
                AwsAssert.True(!c.Rejected, row + " is not rejected (" + c.Reason + ")");
                AwsAssert.Equal(row.Effect, c.Effect, row.ToString());
                AwsAssert.Equal(row.AlwaysRequireApproval, c.AlwaysRequireApproval, row + " always-approval");
                AwsAssert.True(c.Reason.Length > 0, row + " has a reason");
                AwsAssert.True(c.ApprovalSummary.Length > 0, row + " has a summary");
                if (row.SummaryContains != null) AwsAssert.Contains(c.ApprovalSummary, row.SummaryContains, row + " summary");
            }
            foreach (string name in _ToolNames)
                AwsAssert.True(cases.Any(r => r.Tool == name), "classification table covers " + name);
            return Task.CompletedTask;
        }

        private static Task TerminateAlwaysApproves(CancellationToken token)
        {
            ConnectorInvocationContext context = AwsTestSupport.Context(AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint));
            foreach (string action in new string[] { "start", "stop", "reboot", "terminate" })
            {
                EffectClassification c = AwsTestSupport.Classify(context, "aws_ec2_instance_action", "{\"action\":\"" + action + "\",\"instanceIds\":[\"i-0123456789abcdef0\"]}");
                AwsAssert.Equal(action == "terminate", c.AlwaysRequireApproval, action + " always-approval");
                AwsAssert.Equal(action == "start" ? EffectEnum.Write : EffectEnum.Destructive, c.Effect, action + " effect");
                AwsAssert.Equal(AwsOperationClassifier.Classify(AwsEc2InstanceActionTool.OperationFor(action)), c.Effect, action + " matches the operation classifier");
            }
            return Task.CompletedTask;
        }

        private static Task ApprovalSummaryAccount(CancellationToken token)
        {
            ConnectorResourceContext resource = AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint, new Dictionary<string, string> { { "region", "eu-west-2" } });
            AwsIdentityCache.Set(resource, new AwsCallerIdentity { Account = "210987654321", Arn = "arn:aws:iam::210987654321:user/x", Region = "eu-west-2" });
            ConnectorInvocationContext context = AwsTestSupport.Context(resource);
            EffectClassification c = AwsTestSupport.Classify(context, "aws_ec2_instance_action", "{\"action\":\"terminate\",\"instanceIds\":[\"i-0123456789abcdef0\"]}");
            AwsAssert.Contains(c.ApprovalSummary, "account 210987654321", "summary account");
            AwsAssert.Contains(c.ApprovalSummary, "region eu-west-2", "summary region");
            AwsAssert.Contains(c.ApprovalSummary, "aws-prod", "summary binding alias");
            AwsAssert.Contains(c.ApprovalSummary, "i-0123456789abcdef0", "summary instance");
            EffectClassification ecs = AwsTestSupport.Classify(context, "aws_ecs_update_service", "{\"cluster\":\"shop\",\"service\":\"checkout\",\"desiredCount\":3}");
            AwsAssert.Contains(ecs.ApprovalSummary, "checkout", "ecs summary service");
            AwsAssert.Contains(ecs.ApprovalSummary, "cluster shop", "ecs summary cluster");
            AwsAssert.Contains(ecs.ApprovalSummary, "account 210987654321", "ecs summary account");
            AwsAssert.NotContains(c.ApprovalSummary, AwsTestSupport.SecretAccessKey, "summary secret");
            AwsAssert.NotContains(c.ApprovalSummary, AwsTestSupport.AccessKeyId, "summary key id");
            AwsIdentityCache.Remove(resource);
            return Task.CompletedTask;
        }

        private static Task ApprovalSummaryUnconfirmed(CancellationToken token)
        {
            ConnectorInvocationContext context = AwsTestSupport.Context(AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint));
            EffectClassification c = AwsTestSupport.Classify(context, "aws_ec2_instance_action", "{\"action\":\"stop\",\"instanceIds\":[\"i-0123456789abcdef0\"]}");
            AwsAssert.Contains(c.ApprovalSummary, "account not yet confirmed", "summary");
            AwsAssert.Contains(c.ApprovalSummary, "region us-east-1", "summary region");
            return Task.CompletedTask;
        }

        private static Task AllowedServicesClassify(CancellationToken token)
        {
            ConnectorResourceContext resource = AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint, new Dictionary<string, string> { { "allowedServices", "ec2\ncloudwatch" } });
            ConnectorInvocationContext context = AwsTestSupport.Context(resource);
            foreach (IConnectorTool tool in AwsTestSupport.Connector.Tools)
            {
                AwsToolBase aws = (AwsToolBase)tool;
                string args = DefaultArguments(aws.Descriptor.Name);
                EffectClassification c = tool.Classify(context, new ToolArguments(args));
                bool allowed = aws.Service == "ec2" || aws.Service == "cloudwatch" || aws.Service == "sts";
                AwsAssert.Equal(!allowed, c.Rejected, aws.Descriptor.Name + " rejected when service " + aws.Service + " is outside allowedServices");
                if (!allowed) AwsAssert.Contains(c.Reason, "allowedServices", aws.Descriptor.Name + " reason");
            }
            return Task.CompletedTask;
        }

        private static async Task AllowedServicesExecute(CancellationToken token)
        {
            ConnectorResourceContext resource = AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint, new Dictionary<string, string> { { "allowedServices", "ec2" } });
            ConnectorInvocationContext context = AwsTestSupport.Context(resource, TimeSpan.FromSeconds(5));
            foreach (string tool in new string[] { "aws_rds_describe", "aws_lambda_invoke", "aws_ecs_update_service", "aws_logs_tail", "aws_sqs_queue_attributes" })
            {
                ToolResult result = await AwsTestSupport.InvokeAsync(context, tool, DefaultArguments(tool), token).ConfigureAwait(false);
                AwsAssert.Error(result, ConnectorErrorCodes.PermissionDenied, tool);
                AwsAssert.Contains(result.Text, "not allowed", tool + " message");
            }
        }

        private static Task AllowedServicesParsing(CancellationToken token)
        {
            AwsOptions o = AwsOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "allowedServices", "EC2, logs\nRDS\n\n" } }));
            AwsAssert.Equal(3, o.AllowedServices.Count, "parsed count");
            AwsAssert.True(o.IsServiceAllowed("ec2") && o.IsServiceAllowed("logs") && o.IsServiceAllowed("rds"), "listed services allowed");
            AwsAssert.True(!o.IsServiceAllowed("lambda"), "unlisted service refused");
            AwsAssert.True(o.IsServiceAllowed("sts"), "sts always allowed");
            AwsOptions all = AwsOptions.FromSettings(new ResourceSettings(null));
            AwsAssert.True(AwsServiceNames.All.All(s => all.IsServiceAllowed(s)), "empty list allows all");
            return Task.CompletedTask;
        }

        private static async Task ValidateOk(CancellationToken token)
        {
            ConnectorValidationResult keys = await AwsTestSupport.Connector.ValidateAsync(AwsTestSupport.Resource(null), token).ConfigureAwait(false);
            AwsAssert.True(keys.IsValid, "key pair resource valid: " + Describe(keys));
            ConnectorResourceContext role = AwsTestSupport.Resource("http://127.0.0.1:4566", new Dictionary<string, string>
            {
                { "useInstanceRole", "true" },
                { "assumeRoleArn", "arn:aws:iam::123456789012:role/HugoReadOnly" },
                { "externalId", "hugo-ext-123" },
                { "sessionName", "hugo-agent" },
                { "allowedServices", "ec2\nlogs" },
                { "region", "eu-central-2" }
            }, false);
            ConnectorValidationResult ambient = await AwsTestSupport.Connector.ValidateAsync(role, token).ConfigureAwait(false);
            AwsAssert.True(ambient.IsValid, "instance role resource valid: " + Describe(ambient));
        }

        private static async Task ValidateCredentials(CancellationToken token)
        {
            await ExpectInvalid(AwsTestSupport.Resource(null, null, false), "accessKeyPair", "required", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "useInstanceRole", "true" } }), "useInstanceRole", "conflict", token).ConfigureAwait(false);

            ConnectorResourceContext partial = AwsTestSupport.Resource(null, null, false);
            partial.Secrets = new SecretAccessor(new Dictionary<string, Dictionary<string, string>> { { "accessKeyPair", new Dictionary<string, string> { { "accessKeyId", "AKIA" } } } });
            await ExpectInvalid(partial, "accessKeyPair", "invalid_secret", token).ConfigureAwait(false);
        }

        private static async Task ValidateFields(CancellationToken token)
        {
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "region", "Virginia" } }), "region", "invalid_format", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "assumeRoleArn", "arn:aws:iam::123:user/x" } }), "assumeRoleArn", "invalid_format", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "externalId", "abc123" } }), "externalId", "requires_role", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "assumeRoleArn", "arn:aws:iam::123456789012:role/R" }, { "externalId", "bad id with spaces" } }), "externalId", "invalid_format", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "sessionName", "a b" } }), "sessionName", "invalid_format", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "allowedServices", "ec2\ns3" } }), "allowedServices", "invalid_value", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource("ftp://127.0.0.1:21"), "endpointOverride", "invalid_format", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource("http://user:pass@127.0.0.1:4566"), "endpointOverride", "invalid_format", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "maxResults", "0" } }), "maxResults", "out_of_range", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "logsQueryTimeoutSeconds", "301" } }), "logsQueryTimeoutSeconds", "out_of_range", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "assumeRoleDurationSeconds", "60" } }), "assumeRoleDurationSeconds", "out_of_range", token).ConfigureAwait(false);
            await ExpectInvalid(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "maxTimeRangeHours", "many" } }), "maxTimeRangeHours", "invalid_format", token).ConfigureAwait(false);
        }

        private static Task OptionsDefaults(CancellationToken token)
        {
            AwsOptions d = AwsOptions.FromSettings(new ResourceSettings(null));
            AwsAssert.Equal("us-east-1", d.Region, "region");
            AwsAssert.Equal("hugo", d.SessionName, "session name");
            AwsAssert.Equal(3600, d.AssumeRoleDurationSeconds, "duration");
            AwsAssert.Equal(50, d.MaxResults, "max results");
            AwsAssert.Equal(2, d.MaxRetries, "retries");
            AwsAssert.Equal(60, d.DefaultLookbackMinutes, "lookback");
            AwsAssert.Equal(168, d.MaxTimeRangeHours, "range");
            AwsAssert.Equal(30, d.LogsQueryTimeoutSeconds, "logs wait");
            AwsAssert.Equal(200, d.LogsMaxEvents, "logs events");
            AwsAssert.Equal(65536, d.LambdaMaxPayloadBytes, "lambda payload");
            AwsAssert.True(!d.UseInstanceRole && d.AssumeRoleArn == null && d.EndpointOverride == null, "optional values empty");

            AwsOptions c = AwsOptions.FromSettings(new ResourceSettings(new Dictionary<string, string>
            {
                { "maxResults", "99999" }, { "maxRetries", "-3" }, { "assumeRoleDurationSeconds", "5" }, { "logsMaxEvents", "0" }, { "lambdaMaxPayloadBytes", "999999999" }
            }));
            AwsAssert.Equal(1000, c.MaxResults, "max results clamp");
            AwsAssert.Equal(0, c.MaxRetries, "retries clamp");
            AwsAssert.Equal(900, c.AssumeRoleDurationSeconds, "duration clamp");
            AwsAssert.Equal(1, c.LogsMaxEvents, "events clamp");
            AwsAssert.Equal(6291456, c.LambdaMaxPayloadBytes, "payload clamp");
            return Task.CompletedTask;
        }

        private static Task ErrorMapping(CancellationToken token)
        {
            ConnectorException throttled = AwsErrorMapper.Map(Service("Throttling", HttpStatusCode.BadRequest, "Rate exceeded"), "ec2:DescribeInstances");
            AwsAssert.Equal(ConnectorErrorCodes.RateLimited, throttled.Code, "throttling code");
            AwsAssert.True(throttled.RetryAfterSeconds != null && throttled.RetryAfterSeconds.Value > 0, "throttling retry-after");
            AwsAssert.Contains(throttled.Message, "ec2:DescribeInstances", "throttling message names the operation");

            AwsAssert.Equal(ConnectorErrorCodes.PermissionDenied, AwsErrorMapper.Map(Service("AccessDenied", HttpStatusCode.Forbidden, "not authorized"), "x").Code, "AccessDenied");
            AwsAssert.Equal(ConnectorErrorCodes.PermissionDenied, AwsErrorMapper.Map(Service("UnauthorizedOperation", HttpStatusCode.Forbidden, "nope"), "x").Code, "UnauthorizedOperation");
            AwsAssert.Equal(ConnectorErrorCodes.AuthFailed, AwsErrorMapper.Map(Service("InvalidClientTokenId", HttpStatusCode.Forbidden, "bad token"), "x").Code, "InvalidClientTokenId");
            AwsAssert.Equal(ConnectorErrorCodes.AuthFailed, AwsErrorMapper.Map(Service("ExpiredToken", HttpStatusCode.BadRequest, "expired"), "x").Code, "ExpiredToken");
            AwsAssert.Equal(ConnectorErrorCodes.NotFound, AwsErrorMapper.Map(Service("InvalidInstanceID.NotFound", HttpStatusCode.BadRequest, "missing"), "x").Code, "InvalidInstanceID.NotFound");
            AwsAssert.Equal(ConnectorErrorCodes.NotFound, AwsErrorMapper.Map(Service("ResourceNotFoundException", HttpStatusCode.BadRequest, "missing"), "x").Code, "ResourceNotFoundException");
            AwsAssert.Equal(ConnectorErrorCodes.InvalidArgument, AwsErrorMapper.Map(Service("InvalidParameterValue", HttpStatusCode.BadRequest, "bad"), "x").Code, "InvalidParameterValue");
            AwsAssert.Equal(ConnectorErrorCodes.InvalidArgument, AwsErrorMapper.Map(Service("ValidationError", HttpStatusCode.BadRequest, "bad"), "x").Code, "ValidationError");
            AwsAssert.Equal(ConnectorErrorCodes.UpstreamError, AwsErrorMapper.Map(Service("InternalFailure", HttpStatusCode.InternalServerError, "boom"), "x").Code, "InternalFailure");
            AwsAssert.Equal(ConnectorErrorCodes.ConnectionFailed, AwsErrorMapper.Map(new HttpRequestException("Connection refused"), "x").Code, "HttpRequestException");
            AwsAssert.Equal(ConnectorErrorCodes.AuthFailed, AwsErrorMapper.Map(new AmazonClientException("Failed to resolve AWS credentials."), "x").Code, "credential resolution");
            AwsAssert.Equal(ConnectorErrorCodes.InvalidArgument, AwsErrorMapper.Map(new ArgumentException("bad"), "x").Code, "ArgumentException");
            AwsAssert.Equal(ConnectorErrorCodes.PermissionDenied, AwsErrorMapper.Map(new AggregateException(Service("AccessDeniedException", HttpStatusCode.BadRequest, "x")), "x").Code, "aggregate unwrapped");
            ConnectorException passthrough = new ConnectorException("custom", "kept");
            AwsAssert.True(ReferenceEquals(passthrough, AwsErrorMapper.Map(passthrough, "x")), "connector exceptions pass through");
            return Task.CompletedTask;
        }

        private static Task ErrorCodeTable(CancellationToken token)
        {
            object[][] rows = new object[][]
            {
                new object[] { "Throttling", 400, ConnectorErrorCodes.RateLimited },
                new object[] { "ThrottlingException", 400, ConnectorErrorCodes.RateLimited },
                new object[] { "RequestLimitExceeded", 503, ConnectorErrorCodes.RateLimited },
                new object[] { "TooManyRequestsException", 429, ConnectorErrorCodes.RateLimited },
                new object[] { "", 429, ConnectorErrorCodes.RateLimited },
                new object[] { "AccessDeniedException", 400, ConnectorErrorCodes.PermissionDenied },
                new object[] { "", 403, ConnectorErrorCodes.PermissionDenied },
                new object[] { "UnrecognizedClientException", 400, ConnectorErrorCodes.AuthFailed },
                new object[] { "SignatureDoesNotMatch", 403, ConnectorErrorCodes.AuthFailed },
                new object[] { "AuthFailure", 401, ConnectorErrorCodes.AuthFailed },
                new object[] { "DBInstanceNotFound", 404, ConnectorErrorCodes.NotFound },
                new object[] { "DBClusterNotFoundFault", 404, ConnectorErrorCodes.NotFound },
                new object[] { "ClusterNotFoundException", 400, ConnectorErrorCodes.NotFound },
                new object[] { "NoSuchHostedZone", 404, ConnectorErrorCodes.NotFound },
                new object[] { "AWS.SimpleQueueService.NonExistentQueue", 400, ConnectorErrorCodes.NotFound },
                new object[] { "TargetGroupNotFound", 400, ConnectorErrorCodes.NotFound },
                new object[] { "", 404, ConnectorErrorCodes.NotFound },
                new object[] { "InvalidParameterCombination", 400, ConnectorErrorCodes.InvalidArgument },
                new object[] { "InvalidInstanceID.Malformed", 400, ConnectorErrorCodes.InvalidArgument },
                new object[] { "MalformedQueryException", 400, ConnectorErrorCodes.InvalidArgument },
                new object[] { "IncorrectInstanceState", 400, ConnectorErrorCodes.InvalidArgument },
                new object[] { "SomethingOdd", 400, ConnectorErrorCodes.InvalidArgument },
                new object[] { "InternalError", 500, ConnectorErrorCodes.UpstreamError },
                new object[] { "ServiceUnavailable", 503, ConnectorErrorCodes.RateLimited }
            };
            foreach (object[] row in rows)
                AwsAssert.Equal((string)row[2], AwsErrorMapper.CodeFor((string)row[0], (int)row[1]), "code for " + row[0] + "/" + row[1]);
            return Task.CompletedTask;
        }

        private static Task TimeWindow(CancellationToken token)
        {
            AwsOptions options = AwsOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "maxTimeRangeHours", "24" } }));
            DateTime now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            AwsTimeWindow d = AwsTimeWindow.Resolve(new ToolArguments("{}"), options, now);
            AwsAssert.Equal(now.AddMinutes(-60), d.StartUtc, "default lookback");
            AwsAssert.Equal(now, d.EndUtc, "default end");
            AwsTimeWindow l = AwsTimeWindow.Resolve(new ToolArguments("{\"lookbackMinutes\":15}"), options, now);
            AwsAssert.Equal(TimeSpan.FromMinutes(15), l.Length, "lookback length");
            AwsTimeWindow e = AwsTimeWindow.Resolve(new ToolArguments("{\"startTime\":\"2026-10-14T09:00:00Z\",\"endTime\":\"2026-10-14T10:30:00+00:00\"}"), options, now);
            AwsAssert.Equal(new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc), e.StartUtc, "explicit start");
            AwsAssert.Equal(TimeSpan.FromMinutes(90), e.Length, "explicit length");
            AwsTimeWindow offset = AwsTimeWindow.Resolve(new ToolArguments("{\"startTime\":\"2026-10-14T09:00:00-05:00\",\"endTime\":\"2026-10-14T15:00:00Z\"}"), options, now);
            AwsAssert.Equal(new DateTime(2026, 10, 14, 14, 0, 0, DateTimeKind.Utc), offset.StartUtc, "offset converted to UTC");
            ExpectCode(() => AwsTimeWindow.Resolve(new ToolArguments("{\"startTime\":\"2026-10-14T11:00:00Z\",\"endTime\":\"2026-10-14T10:00:00Z\"}"), options, now), ConnectorErrorCodes.InvalidArgument, "inverted");
            ExpectCode(() => AwsTimeWindow.Resolve(new ToolArguments("{\"lookbackMinutes\":2000}"), options, now), ConnectorErrorCodes.TimeRangeExceeded, "too long");
            ExpectCode(() => AwsTimeWindow.Resolve(new ToolArguments("{\"startTime\":\"yesterday\"}"), options, now), ConnectorErrorCodes.InvalidArgument, "malformed");
            return Task.CompletedTask;
        }

        private static Task MetricAutoPeriod(CancellationToken token)
        {
            AwsCloudWatchGetMetricDataTool tool = new AwsCloudWatchGetMetricDataTool();
            AwsAssert.Equal(60, tool.AutoPeriod(TimeSpan.FromMinutes(30)), "30 minutes");
            AwsAssert.Equal(60, tool.AutoPeriod(TimeSpan.FromHours(2)), "2 hours");
            AwsAssert.Equal(180, tool.AutoPeriod(TimeSpan.FromHours(6)), "6 hours");
            AwsAssert.Equal(720, tool.AutoPeriod(TimeSpan.FromHours(24)), "24 hours");
            AwsAssert.Equal(5040, tool.AutoPeriod(TimeSpan.FromDays(7)), "7 days");
            return Task.CompletedTask;
        }

        private static Task Arguments(CancellationToken token)
        {
            AwsAssert.True(AwsArguments.IsInstanceId("i-0123456789abcdef0"), "17-digit ID");
            AwsAssert.True(AwsArguments.IsInstanceId("i-01234567"), "8-digit ID");
            AwsAssert.True(!AwsArguments.IsInstanceId("i-0123"), "short ID");
            AwsAssert.True(!AwsArguments.IsInstanceId("i-0123456789ABCDEF0"), "uppercase ID");
            AwsAssert.True(!AwsArguments.IsInstanceId("web-1"), "name");
            AwsAssert.Equal(2, AwsArguments.InstanceIds(new string[] { " i-01234567 ", "i-01234567", "i-0123456789abcdef0", "" }).Count, "dedupe and trim");
            ExpectCode(() => AwsArguments.InstanceIds(new string[] { "i-xyz" }), ConnectorErrorCodes.InvalidArgument, "malformed ID");

            AwsAssert.Equal(2, AwsArguments.Filters(new ToolArguments("{\"filters\":[{\"name\":\"tag:env\",\"values\":[\"prod\"]},{\"Name\":\"vpc-id\",\"Values\":[\"vpc-1\",\"vpc-2\"]}]}")).Count, "filters parse");
            ExpectCode(() => AwsArguments.Filters(new ToolArguments("{\"filters\":[{\"name\":\"tag:env\",\"values\":[]}]}")), ConnectorErrorCodes.InvalidArgument, "empty filter values");
            ExpectCode(() => AwsArguments.Filters(new ToolArguments("{\"filters\":{\"name\":\"x\"}}")), ConnectorErrorCodes.InvalidArgument, "filters wrong shape");

            AwsAssert.Equal("{}", AwsLambdaInvokeTool.PayloadOf(new ToolArguments("{}")), "default payload");
            AwsAssert.Equal("{\"a\":1}", AwsLambdaInvokeTool.PayloadOf(new ToolArguments("{\"payload\":{\"a\":1}}")), "object payload");
            AwsAssert.Equal("[1,2]", AwsLambdaInvokeTool.PayloadOf(new ToolArguments("{\"payload\":\"[1,2]\"}")), "string payload");
            ExpectCode(() => AwsLambdaInvokeTool.PayloadOf(new ToolArguments("{\"payload\":\"{oops\"}")), ConnectorErrorCodes.InvalidArgument, "invalid string payload");

            AwsAssert.Equal("www.example.com.|A|", AwsRoute53ListRecordsTool.EncodeToken("www.example.com.", "A", null), "route53 token");
            AwsAssert.Equal(null, AwsRoute53ListRecordsTool.EncodeToken(null, "A", null), "no route53 token");
            AwsAssert.Equal("arn:aws:ec2:x|AAAA|blue", AwsRoute53ListRecordsTool.EncodeToken("arn:aws:ec2:x", "AAAA", "blue"), "route53 token with identifier");
            return Task.CompletedTask;
        }

        private static Task EndpointParse(CancellationToken token)
        {
            AwsAssert.Equal("http://127.0.0.1:4566/", AwsSessionFactory.ParseEndpoint("http://127.0.0.1:4566").ToString(), "http endpoint");
            AwsAssert.Equal("https://vpce-1.ec2.us-east-1.vpce.amazonaws.com/", AwsSessionFactory.ParseEndpoint(" https://vpce-1.ec2.us-east-1.vpce.amazonaws.com ").ToString(), "https endpoint");
            ExpectCode(() => AwsSessionFactory.ParseEndpoint("127.0.0.1:4566"), ConnectorErrorCodes.InvalidSetting, "no scheme");
            ExpectCode(() => AwsSessionFactory.ParseEndpoint("http://127.0.0.1:4566/?a=b"), ConnectorErrorCodes.InvalidSetting, "query string");
            ExpectCode(() => AwsSessionFactory.ParseEndpoint(""), ConnectorErrorCodes.InvalidSetting, "empty");
            return Task.CompletedTask;
        }

        private static async Task ExecuteSecretMissing(CancellationToken token)
        {
            ConnectorInvocationContext context = AwsTestSupport.Context(AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint, null, false), TimeSpan.FromSeconds(5));
            ToolResult result = await AwsTestSupport.InvokeAsync(context, "aws_ec2_describe_instances", "{}", token).ConfigureAwait(false);
            AwsAssert.Error(result, ConnectorErrorCodes.SecretMissing, "describe without credentials");
        }

        private static async Task TestConnectionInvalid(CancellationToken token)
        {
            ConnectorHealthResult health = await AwsTestSupport.Connector.TestConnectionAsync(AwsTestSupport.Resource(null, new Dictionary<string, string> { { "region", "nowhere" } }), token).ConfigureAwait(false);
            AwsAssert.True(!health.Success, "invalid resource fails");
            AwsAssert.Equal(ConnectorErrorCodes.InvalidSetting, health.ErrorCode, "code");
            AwsAssert.Contains(health.Message, "region", "message names the field");
        }

        private static async Task TestConnectionUnreachable(CancellationToken token)
        {
            ConnectorHealthResult health = await AwsTestSupport.Connector.TestConnectionAsync(AwsTestSupport.Resource(AwsTestSupport.ClosedEndpoint), token).ConfigureAwait(false);
            AwsAssert.True(!health.Success, "closed port fails");
            AwsAssert.Equal(ConnectorErrorCodes.ConnectionFailed, health.ErrorCode, "code (" + health.Message + ")");
            AwsAssert.NotContains(health.Message, AwsTestSupport.SecretAccessKey, "no secret in message");
        }

        private static string DefaultArguments(string tool)
        {
            switch (tool)
            {
                case "aws_ec2_instance_action":
                    return "{\"action\":\"start\",\"instanceIds\":[\"i-0123456789abcdef0\"]}";
                case "aws_cloudwatch_get_metric_data":
                    return "{\"metrics\":[{\"namespace\":\"AWS/EC2\",\"metricName\":\"CPUUtilization\"}]}";
                case "aws_logs_insights_query":
                    return "{\"logGroupNames\":[\"/app\"],\"query\":\"fields @message\"}";
                case "aws_logs_tail":
                    return "{\"logGroupName\":\"/app\"}";
                case "aws_ecs_update_service":
                    return "{\"service\":\"checkout\",\"desiredCount\":2}";
                case "aws_lambda_invoke":
                    return "{\"functionName\":\"f\"}";
                case "aws_sqs_queue_attributes":
                    return "{\"queueName\":\"orders\"}";
                default:
                    return "{}";
            }
        }

        private static AmazonServiceException Service(string code, HttpStatusCode status, string message)
        {
            return new AmazonServiceException(message, null, ErrorType.Sender, code, "req-1", status);
        }

        private static async Task ExpectInvalid(ConnectorResourceContext resource, string field, string code, CancellationToken token)
        {
            ConnectorValidationResult result = await AwsTestSupport.Connector.ValidateAsync(resource, token).ConfigureAwait(false);
            AwsAssert.True(result.Errors.Any(e => e.Field == field && e.Code == code), "expected " + field + "/" + code + " but got: " + Describe(result));
        }

        private static void ExpectCode(Action action, string code, string what)
        {
            try
            {
                action();
            }
            catch (ConnectorException e)
            {
                AwsAssert.Equal(code, e.Code, what);
                return;
            }
            throw new InvalidOperationException("Assertion failed: " + what + " should throw " + code);
        }

        private static string Describe(ConnectorValidationResult result)
        {
            return result.IsValid ? "(valid)" : String.Join("; ", result.Errors.Select(e => e.Field + "/" + e.Code + ": " + e.Message));
        }

        #endregion
    }
}
