namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Standard error codes for <see cref="ConnectorException"/>, <see cref="ToolResult.Error"/>, and
    /// <see cref="ConnectorHealthResult.Fail"/>. Connectors may define additional codes, but should prefer these so
    /// agents, the dashboard, and translations can rely on a stable set.
    /// </summary>
    public static class ConnectorErrorCodes
    {
        /// <summary>The target could not be reached (DNS, TCP, TLS).</summary>
        public const string ConnectionFailed = "connection_failed";

        /// <summary>The upstream rejected the credential.</summary>
        public const string AuthFailed = "auth_failed";

        /// <summary>The credential is valid but lacks access, or the request falls outside a resource restriction.</summary>
        public const string PermissionDenied = "permission_denied";

        /// <summary>The call did not finish within its timeout.</summary>
        public const string Timeout = "timeout";

        /// <summary>The call was cancelled by the caller.</summary>
        public const string Cancelled = "cancelled";

        /// <summary>The requested object, file, table, or entity does not exist.</summary>
        public const string NotFound = "not_found";

        /// <summary>The target already exists and the call did not ask to replace it.</summary>
        public const string AlreadyExists = "already_exists";

        /// <summary>A tool argument is missing or invalid.</summary>
        public const string InvalidArgument = "invalid_argument";

        /// <summary>A resource setting is missing or invalid.</summary>
        public const string InvalidSetting = "invalid_setting";

        /// <summary>A required secret is not bound to the resource.</summary>
        public const string SecretMissing = "secret_missing";

        /// <summary>The upstream throttled the request; see <see cref="ToolResult.RetryAfterSeconds"/>.</summary>
        public const string RateLimited = "rate_limited";

        /// <summary>The object or result is larger than allowed.</summary>
        public const string TooLarge = "too_large";

        /// <summary>A requested time range exceeds the resource's limit.</summary>
        public const string TimeRangeExceeded = "time_range_exceeded";

        /// <summary>A scan stopped at its cap before finishing.</summary>
        public const string ScanLimit = "scan_limit";

        /// <summary>The operation is not supported by this provider or target.</summary>
        public const string NotSupported = "not_supported";

        /// <summary>The SSH host key does not match the pinned fingerprint.</summary>
        public const string HostKeyMismatch = "host_key_mismatch";

        /// <summary>The upstream returned an unexpected error.</summary>
        public const string UpstreamError = "upstream_error";
    }
}
