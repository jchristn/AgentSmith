namespace Hugo.Connectors.Aws
{
    /// <summary>
    /// Setting keys, secret slot names, and secret field names of the <c>aws</c> connector.
    /// </summary>
    public static class AwsSettingKeys
    {
        /// <summary>
        /// AWS region, for example <c>us-east-1</c>.
        /// </summary>
        public const string Region = "region";

        /// <summary>
        /// ARN of a role to assume with STS before every session.
        /// </summary>
        public const string AssumeRoleArn = "assumeRoleArn";

        /// <summary>
        /// External ID passed to sts:AssumeRole.
        /// </summary>
        public const string ExternalId = "externalId";

        /// <summary>
        /// Role session name passed to sts:AssumeRole.
        /// </summary>
        public const string SessionName = "sessionName";

        /// <summary>
        /// Duration of assumed role sessions, in seconds.
        /// </summary>
        public const string AssumeRoleDurationSeconds = "assumeRoleDurationSeconds";

        /// <summary>
        /// Use the ambient credential chain (environment, profile, container or instance role).
        /// </summary>
        public const string UseInstanceRole = "useInstanceRole";

        /// <summary>
        /// Services tools may call (string list, empty means all).
        /// </summary>
        public const string AllowedServices = "allowedServices";

        /// <summary>
        /// Endpoint URL used for every service (LocalStack, VPC endpoints, test stubs).
        /// </summary>
        public const string EndpointOverride = "endpointOverride";

        /// <summary>
        /// Default page size of list and describe tools.
        /// </summary>
        public const string MaxResults = "maxResults";

        /// <summary>
        /// SDK retries for throttled or transient failures.
        /// </summary>
        public const string MaxRetries = "maxRetries";

        /// <summary>
        /// Default lookback window, in minutes, for metric and log tools.
        /// </summary>
        public const string DefaultLookbackMinutes = "defaultLookbackMinutes";

        /// <summary>
        /// Largest time window, in hours, a metric or log call may cover.
        /// </summary>
        public const string MaxTimeRangeHours = "maxTimeRangeHours";

        /// <summary>
        /// Longest time aws_logs_insights_query waits for a query, in seconds.
        /// </summary>
        public const string LogsQueryTimeoutSeconds = "logsQueryTimeoutSeconds";

        /// <summary>
        /// Most log events aws_logs_tail returns.
        /// </summary>
        public const string LogsMaxEvents = "logsMaxEvents";

        /// <summary>
        /// Largest request payload aws_lambda_invoke sends, in bytes.
        /// </summary>
        public const string LambdaMaxPayloadBytes = "lambdaMaxPayloadBytes";

        /// <summary>
        /// Secret slot holding the access key pair.
        /// </summary>
        public const string SlotAccessKeyPair = "accessKeyPair";

        /// <summary>
        /// Access key ID field.
        /// </summary>
        public const string FieldAccessKeyId = "accessKeyId";

        /// <summary>
        /// Secret access key field.
        /// </summary>
        public const string FieldSecretAccessKey = "secretAccessKey";

        /// <summary>
        /// Optional session token field (temporary credentials).
        /// </summary>
        public const string FieldSessionToken = "sessionToken";
    }
}
