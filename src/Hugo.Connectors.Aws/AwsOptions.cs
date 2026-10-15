namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed view of an <c>aws</c> resource's settings with defaults applied and values clamped to their ranges.
    /// Instances are immutable after <see cref="FromSettings"/>. Thread-safe for reads.
    /// </summary>
    public class AwsOptions
    {
        #region Public-Members

        /// <summary>
        /// Default region when the setting is empty.
        /// </summary>
        public const string DefaultRegion = "us-east-1";

        /// <summary>
        /// Default role session name.
        /// </summary>
        public const string DefaultSessionName = "hugo";

        /// <summary>
        /// Region. Default <c>us-east-1</c>.
        /// </summary>
        public string Region { get; set; } = DefaultRegion;

        /// <summary>
        /// Role to assume, or null.
        /// </summary>
        public string? AssumeRoleArn { get; set; } = null;

        /// <summary>
        /// External ID for sts:AssumeRole, or null.
        /// </summary>
        public string? ExternalId { get; set; } = null;

        /// <summary>
        /// Role session name. Default <c>hugo</c>.
        /// </summary>
        public string SessionName { get; set; } = DefaultSessionName;

        /// <summary>
        /// Assumed role session duration in seconds. Default 3600, minimum 900, maximum 43200.
        /// </summary>
        public int AssumeRoleDurationSeconds
        {
            get
            {
                return _AssumeRoleDurationSeconds;
            }
            set
            {
                _AssumeRoleDurationSeconds = Math.Clamp(value, 900, 43200);
            }
        }

        /// <summary>
        /// Use the ambient credential chain instead of the access key pair. Default false.
        /// </summary>
        public bool UseInstanceRole { get; set; } = false;

        /// <summary>
        /// Allowed services (lowercase). Empty means every service.
        /// </summary>
        public List<string> AllowedServices
        {
            get
            {
                return _AllowedServices;
            }
            set
            {
                _AllowedServices = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Endpoint override for every service, or null for the public AWS endpoints.
        /// </summary>
        public string? EndpointOverride { get; set; } = null;

        /// <summary>
        /// Default page size of list and describe tools. Default 50, minimum 1, maximum 1000.
        /// </summary>
        public int MaxResults
        {
            get
            {
                return _MaxResults;
            }
            set
            {
                _MaxResults = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// SDK retries for throttling and transient errors. Default 2, minimum 0, maximum 10.
        /// </summary>
        public int MaxRetries
        {
            get
            {
                return _MaxRetries;
            }
            set
            {
                _MaxRetries = Math.Clamp(value, 0, 10);
            }
        }

        /// <summary>
        /// Lookback window, in minutes, when a metric or log call gives no start time. Default 60, minimum 1, maximum 20160.
        /// </summary>
        public int DefaultLookbackMinutes
        {
            get
            {
                return _DefaultLookbackMinutes;
            }
            set
            {
                _DefaultLookbackMinutes = Math.Clamp(value, 1, 20160);
            }
        }

        /// <summary>
        /// Largest window, in hours, a metric or log call may cover. Default 168, minimum 1, maximum 2160.
        /// </summary>
        public int MaxTimeRangeHours
        {
            get
            {
                return _MaxTimeRangeHours;
            }
            set
            {
                _MaxTimeRangeHours = Math.Clamp(value, 1, 2160);
            }
        }

        /// <summary>
        /// Longest wait for a Logs Insights query, in seconds. Default 30, minimum 1, maximum 300.
        /// </summary>
        public int LogsQueryTimeoutSeconds
        {
            get
            {
                return _LogsQueryTimeoutSeconds;
            }
            set
            {
                _LogsQueryTimeoutSeconds = Math.Clamp(value, 1, 300);
            }
        }

        /// <summary>
        /// Most events aws_logs_tail returns. Default 200, minimum 1, maximum 10000.
        /// </summary>
        public int LogsMaxEvents
        {
            get
            {
                return _LogsMaxEvents;
            }
            set
            {
                _LogsMaxEvents = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Largest aws_lambda_invoke request payload, in bytes. Default 65536, minimum 2, maximum 6291456 (the AWS limit).
        /// </summary>
        public int LambdaMaxPayloadBytes
        {
            get
            {
                return _LambdaMaxPayloadBytes;
            }
            set
            {
                _LambdaMaxPayloadBytes = Math.Clamp(value, 2, 6291456);
            }
        }

        #endregion

        #region Private-Members

        private int _AssumeRoleDurationSeconds = 3600;
        private List<string> _AllowedServices = new List<string>();
        private int _MaxResults = 50;
        private int _MaxRetries = 2;
        private int _DefaultLookbackMinutes = 60;
        private int _MaxTimeRangeHours = 168;
        private int _LogsQueryTimeoutSeconds = 30;
        private int _LogsMaxEvents = 200;
        private int _LambdaMaxPayloadBytes = 65536;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build options from resource settings. Missing values take their defaults and numbers are clamped.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static AwsOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            AwsOptions o = new AwsOptions();
            o.Region = (settings.GetString(AwsSettingKeys.Region) ?? DefaultRegion).Trim();
            o.AssumeRoleArn = Trimmed(settings.GetString(AwsSettingKeys.AssumeRoleArn));
            o.ExternalId = Trimmed(settings.GetString(AwsSettingKeys.ExternalId));
            o.SessionName = Trimmed(settings.GetString(AwsSettingKeys.SessionName)) ?? DefaultSessionName;
            o.AssumeRoleDurationSeconds = settings.GetInt(AwsSettingKeys.AssumeRoleDurationSeconds, 3600);
            o.UseInstanceRole = settings.GetBool(AwsSettingKeys.UseInstanceRole, false);
            o.AllowedServices = settings.GetStringList(AwsSettingKeys.AllowedServices)
                .SelectMany(s => s.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(s => s.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            o.EndpointOverride = Trimmed(settings.GetString(AwsSettingKeys.EndpointOverride));
            o.MaxResults = settings.GetInt(AwsSettingKeys.MaxResults, 50);
            o.MaxRetries = settings.GetInt(AwsSettingKeys.MaxRetries, 2);
            o.DefaultLookbackMinutes = settings.GetInt(AwsSettingKeys.DefaultLookbackMinutes, 60);
            o.MaxTimeRangeHours = settings.GetInt(AwsSettingKeys.MaxTimeRangeHours, 168);
            o.LogsQueryTimeoutSeconds = settings.GetInt(AwsSettingKeys.LogsQueryTimeoutSeconds, 30);
            o.LogsMaxEvents = settings.GetInt(AwsSettingKeys.LogsMaxEvents, 200);
            o.LambdaMaxPayloadBytes = settings.GetInt(AwsSettingKeys.LambdaMaxPayloadBytes, 65536);
            return o;
        }

        /// <summary>
        /// Whether a service is allowed for this resource.
        /// </summary>
        /// <param name="service">Service name from <see cref="AwsServiceNames"/>.</param>
        /// <returns>True when allowed.</returns>
        public bool IsServiceAllowed(string service)
        {
            return AwsServiceNames.IsAllowed(_AllowedServices, service);
        }

        #endregion

        #region Private-Methods

        private static string? Trimmed(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return value.Trim();
        }

        #endregion
    }
}
