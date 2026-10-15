namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Tools;

    /// <summary>
    /// The <c>aws</c> connector (family <c>aws</c>): read-mostly access to the AWS control plane (STS, EC2, Auto Scaling,
    /// CloudWatch, CloudWatch Logs, ECS, RDS, Lambda, ELBv2, Route 53, SQS, SNS) with an access key pair or the ambient
    /// credential chain, optional role assumption with an external ID, a per-resource service allow list, and an endpoint
    /// override for LocalStack or VPC endpoints. Thread-safe; one instance serves every resource.
    /// </summary>
    public class AwsConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public const string ConnectorType = "aws";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Time limit for <see cref="TestConnectionAsync"/>. Minimum 1 second, maximum 5 minutes, default 20 seconds.
        /// </summary>
        public TimeSpan TestTimeout
        {
            get
            {
                return _TestTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(5)) value = TimeSpan.FromMinutes(5);
                _TestTimeout = value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _Region = new Regex("^[a-z]{2}(-[a-z0-9]+)+-[0-9]{1,2}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex _RoleArn = new Regex("^arn:aws[a-z-]*:iam::[0-9]{12}:role/[\\w+=,.@/-]{1,512}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex _ExternalId = new Regex("^[\\w+=,.@:/-]{2,1224}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex _SessionName = new Regex("^[\\w+=,.@-]{2,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private TimeSpan _TestTimeout = TimeSpan.FromSeconds(20);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new AwsCallerIdentityTool(),
                new AwsEc2DescribeInstancesTool(),
                new AwsEc2InstanceActionTool(),
                new AwsAutoScalingDescribeGroupsTool(),
                new AwsCloudWatchGetMetricDataTool(),
                new AwsCloudWatchListAlarmsTool(),
                new AwsLogsInsightsQueryTool(),
                new AwsLogsTailTool(),
                new AwsEcsDescribeServicesTool(),
                new AwsEcsUpdateServiceTool(),
                new AwsRdsDescribeTool(),
                new AwsLambdaListFunctionsTool(),
                new AwsLambdaInvokeTool(),
                new AwsElbTargetHealthTool(),
                new AwsRoute53ListRecordsTool(),
                new AwsSqsQueueAttributesTool(),
                new AwsSnsListTopicsTool()
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            ConnectorValidationResult result = new ConnectorValidationResult();
            ResourceSettings s = resource.Settings;

            string region = (s.GetString(AwsSettingKeys.Region) ?? AwsOptions.DefaultRegion).Trim();
            if (!_Region.IsMatch(region)) result.Add(AwsSettingKeys.Region, "invalid_format", "The region must look like us-east-1 or eu-central-2.");

            string? roleArn = s.GetString(AwsSettingKeys.AssumeRoleArn);
            if (!String.IsNullOrWhiteSpace(roleArn) && !_RoleArn.IsMatch(roleArn.Trim()))
                result.Add(AwsSettingKeys.AssumeRoleArn, "invalid_format", "assumeRoleArn must be an IAM role ARN such as arn:aws:iam::123456789012:role/HugoReadOnly.");
            string? externalId = s.GetString(AwsSettingKeys.ExternalId);
            if (!String.IsNullOrWhiteSpace(externalId))
            {
                if (String.IsNullOrWhiteSpace(roleArn)) result.Add(AwsSettingKeys.ExternalId, "requires_role", "externalId only applies with assumeRoleArn.");
                else if (!_ExternalId.IsMatch(externalId.Trim())) result.Add(AwsSettingKeys.ExternalId, "invalid_format", "externalId must be 2 to 1224 characters of letters, digits, and +=,.@:/-_.");
            }
            string? sessionName = s.GetString(AwsSettingKeys.SessionName);
            if (!String.IsNullOrWhiteSpace(sessionName) && !_SessionName.IsMatch(sessionName.Trim()))
                result.Add(AwsSettingKeys.SessionName, "invalid_format", "sessionName must be 2 to 64 characters of letters, digits, and +=,.@-_.");
            CheckRange(result, s, AwsSettingKeys.AssumeRoleDurationSeconds, 900, 43200);

            bool instanceRole = s.GetBool(AwsSettingKeys.UseInstanceRole, false);
            bool hasSlot = resource.Secrets.HasSlot(AwsSettingKeys.SlotAccessKeyPair);
            string? id = resource.Secrets.Get(AwsSettingKeys.SlotAccessKeyPair, AwsSettingKeys.FieldAccessKeyId);
            string? secret = resource.Secrets.Get(AwsSettingKeys.SlotAccessKeyPair, AwsSettingKeys.FieldSecretAccessKey);
            if (hasSlot && (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(secret)))
                result.Add(AwsSettingKeys.SlotAccessKeyPair, "invalid_secret", "The access key pair must have both accessKeyId and secretAccessKey.");
            else if (hasSlot && instanceRole)
                result.Add(AwsSettingKeys.UseInstanceRole, "conflict", "Choose one credential source: unbind the accessKeyPair secret or turn off useInstanceRole.");
            else if (!hasSlot && !instanceRole)
                result.Add(AwsSettingKeys.SlotAccessKeyPair, "required", "Bind an AccessKeyPair secret to the 'accessKeyPair' slot, or turn on useInstanceRole to use the server's ambient credentials.");

            List<string> unknown = s.GetStringList(AwsSettingKeys.AllowedServices)
                .SelectMany(v => v.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(v => !AwsServiceNames.IsKnown(v))
                .ToList();
            if (unknown.Count > 0)
                result.Add(AwsSettingKeys.AllowedServices, "invalid_value", "Unknown services: " + String.Join(", ", unknown) + ". Known services: " + String.Join(", ", AwsServiceNames.All) + ".");

            if (s.Has(AwsSettingKeys.EndpointOverride))
            {
                try
                {
                    AwsSessionFactory.ParseEndpoint(s.GetString(AwsSettingKeys.EndpointOverride));
                }
                catch (ConnectorException e)
                {
                    result.Add(AwsSettingKeys.EndpointOverride, "invalid_format", e.Message);
                }
            }

            CheckRange(result, s, AwsSettingKeys.MaxResults, 1, 1000);
            CheckRange(result, s, AwsSettingKeys.MaxRetries, 0, 10);
            CheckRange(result, s, AwsSettingKeys.DefaultLookbackMinutes, 1, 20160);
            CheckRange(result, s, AwsSettingKeys.MaxTimeRangeHours, 1, 2160);
            CheckRange(result, s, AwsSettingKeys.LogsQueryTimeoutSeconds, 1, 300);
            CheckRange(result, s, AwsSettingKeys.LogsMaxEvents, 1, 10000);
            CheckRange(result, s, AwsSettingKeys.LambdaMaxPayloadBytes, 2, 6291456);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch stopwatch = Stopwatch.StartNew();
            ConnectorValidationResult validation = await ValidateAsync(resource, token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                ConnectorValidationError first = validation.Errors[0];
                return ConnectorHealthResult.Fail(ConnectorErrorCodes.InvalidSetting, first.Field + ": " + first.Message, 0);
            }

            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(_TestTimeout);
                try
                {
                    using (AwsSession session = await AwsSessionFactory.CreateAsync(resource, cts.Token).ConfigureAwait(false))
                    {
                        AwsCallerIdentity identity = await AwsSessionFactory.ResolveIdentityAsync(resource, session, cts.Token).ConfigureAwait(false);
                        double latency = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
                        ConnectorHealthResult ok = ConnectorHealthResult.Ok("Authenticated to account " + identity.Account + " as " + identity.Arn + " in " + session.Options.Region + ".", latency);
                        ok.Details["account"] = identity.Account;
                        ok.Details["arn"] = identity.Arn;
                        ok.Details["region"] = session.Options.Region;
                        ok.Details["allowedServices"] = session.Options.AllowedServices.Count == 0 ? "all" : String.Join(",", session.Options.AllowedServices);
                        if (!String.IsNullOrEmpty(session.Options.EndpointOverride)) ok.Details["endpointOverride"] = session.Options.EndpointOverride;
                        if (!String.IsNullOrEmpty(session.Options.AssumeRoleArn)) ok.Details["assumedRole"] = session.Options.AssumeRoleArn;
                        ok.Details["latencyMs"] = latency.ToString(CultureInfo.InvariantCulture);
                        return ok;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "sts:GetCallerIdentity did not answer within " + _TestTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " seconds.", stopwatch.Elapsed.TotalMilliseconds);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    ConnectorException mapped = AwsErrorMapper.Map(e, "sts:GetCallerIdentity");
                    return ConnectorHealthResult.Fail(mapped.Code, mapped.Message, stopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void CheckRange(ConnectorValidationResult result, ResourceSettings s, string key, long minimum, long maximum)
        {
            string? raw = s.GetString(key);
            if (raw == null) return;
            if (!Int64.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                result.Add(key, "invalid_format", key + " must be a whole number.");
                return;
            }
            if (value < minimum || value > maximum)
                result.Add(key, "out_of_range", key + " must be between " + minimum.ToString(CultureInfo.InvariantCulture) + " and " + maximum.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor(AwsSettingKeys.Region, SettingValueKindEnum.String, "Region") { Required = true, DefaultValue = AwsOptions.DefaultRegion, Pattern = "^[a-z]{2}(-[a-z0-9]+)+-[0-9]{1,2}$", Group = "Connection", ShowInSummary = true, Help = "AWS region every tool uses, for example us-east-1. Default us-east-1. Bind one resource per region." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.UseInstanceRole, SettingValueKindEnum.Boolean, "Use ambient credentials") { DefaultValue = "false", Group = "Credentials", Help = "When true, credentials come from the Hugo server's environment (environment variables, shared profile, ECS task role, or EC2 instance role) instead of the accessKeyPair secret. Default false." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.AssumeRoleArn, SettingValueKindEnum.String, "Role to assume") { Pattern = "^arn:aws[a-z-]*:iam::[0-9]{12}:role/.+$", Group = "Credentials", ShowInSummary = true, Help = "Optional IAM role ARN. The base credentials call sts:AssumeRole and every tool uses the temporary role credentials, refreshed before they expire. Use this for cross-account access." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.ExternalId, SettingValueKindEnum.String, "External ID") { Group = "Credentials", Help = "External ID required by the role's trust policy (confused deputy protection). Only used with a role to assume." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.SessionName, SettingValueKindEnum.String, "Role session name") { DefaultValue = AwsOptions.DefaultSessionName, Pattern = "^[\\w+=,.@-]{2,64}$", Group = "Credentials", Help = "Session name recorded in CloudTrail for assumed role calls. Default hugo." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.AssumeRoleDurationSeconds, SettingValueKindEnum.Integer, "Role session duration (seconds)") { DefaultValue = "3600", Minimum = 900, Maximum = 43200, Group = "Credentials", Help = "Lifetime of assumed role credentials. Default 3600, minimum 900, maximum 43200 (the role's maximum session duration also applies)." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.AllowedServices, SettingValueKindEnum.StringList, "Allowed services") { Group = "Safety", ShowInSummary = true, Help = "Services tools may call, one per line: ec2, autoscaling, cloudwatch, logs, ecs, rds, lambda, elb, route53, sqs, sns. Empty allows every service. Tools for other services are refused. aws_caller_identity (sts) is always allowed." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.EndpointOverride, SettingValueKindEnum.String, "Endpoint override") { Group = "Connection", Help = "Optional URL used for every service instead of the public AWS endpoints, for example http://127.0.0.1:4566 for LocalStack. Leave empty for AWS." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.MaxResults, SettingValueKindEnum.Integer, "Default page size") { DefaultValue = "50", Minimum = 1, Maximum = 1000, Group = "Limits", Help = "Rows a list or describe tool asks for when the call does not say, clamped to each API's range and to the call's row limit. Default 50." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.MaxRetries, SettingValueKindEnum.Integer, "SDK retries") { DefaultValue = "2", Minimum = 0, Maximum = 10, Group = "Limits", Help = "Retries the AWS SDK makes for throttling and transient errors before a tool reports rate_limited. Default 2." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.DefaultLookbackMinutes, SettingValueKindEnum.Integer, "Default lookback (minutes)") { DefaultValue = "60", Minimum = 1, Maximum = 20160, Group = "Limits", Help = "Window metric and log tools use when a call gives no start time. Default 60." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.MaxTimeRangeHours, SettingValueKindEnum.Integer, "Maximum time range (hours)") { DefaultValue = "168", Minimum = 1, Maximum = 2160, Group = "Limits", Help = "Longest window a metric, log, or RDS event call may cover; longer calls fail with time_range_exceeded. Logs Insights bills by bytes scanned. Default 168 (7 days)." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.LogsQueryTimeoutSeconds, SettingValueKindEnum.Integer, "Logs Insights wait (seconds)") { DefaultValue = "30", Minimum = 1, Maximum = 300, Group = "Limits", Help = "Longest aws_logs_insights_query waits for a query before returning its ID for a later poll. Default 30." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.LogsMaxEvents, SettingValueKindEnum.Integer, "Maximum log events") { DefaultValue = "200", Minimum = 1, Maximum = 10000, Group = "Limits", Help = "Most events aws_logs_tail returns. The call's line limit also applies. Default 200." });
            schema.Add(new SettingDescriptor(AwsSettingKeys.LambdaMaxPayloadBytes, SettingValueKindEnum.Integer, "Maximum Lambda payload (bytes)") { DefaultValue = "65536", Minimum = 2, Maximum = 6291456, Group = "Limits", Help = "Largest request payload aws_lambda_invoke sends. Default 65536; AWS allows 6291456 for synchronous calls." });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = ConnectorType;
            d.Family = "aws";
            d.DisplayName = "AWS";
            d.Description = "The AWS control plane for one account and region: confirm identity, inspect EC2, Auto Scaling, ECS, RDS, Lambda, load balancers, Route 53, SQS, and SNS, read CloudWatch metrics, alarms, and logs, and perform a few operational actions (instance start, stop, reboot, terminate; ECS scale and redeploy; Lambda invoke) behind approvals.";
            d.Category = ConnectorCategoryEnum.Cloud;
            d.IconKey = "cloud";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(AwsSettingKeys.SlotAccessKeyPair, "Access key pair", SecretKindEnum.AccessKeyPair)
                {
                    Help = "IAM access key: accessKeyId and secretAccessKey, plus sessionToken for temporary credentials. Grant only the actions the bindings need; see docs/connectors/aws-iam-policies.md for read-only and operator policies. Not needed when useInstanceRole is on.",
                    Required = false
                }
            };
            d.DefaultInstructionsKey = "connector.aws.instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(AwsConnector).Assembly, "connector.aws.instructions.md");
            d.DocumentationUrl = "https://docs.aws.amazon.com/";
            return d;
        }

        #endregion
    }
}
