namespace Hugo.Connectors.Redis
{
    /// <summary>
    /// Setting keys and secret slot names of the <c>redis</c> connector. These are part of the resource contract and are
    /// not configurable. Thread-safe.
    /// </summary>
    public static class RedisSettingKeys
    {
        /// <summary>Endpoints, one <c>host:port</c> per line. Sentinel endpoints when the cluster mode is Sentinel.</summary>
        public const string Endpoints = "endpoints";

        /// <summary>Whether to connect with TLS.</summary>
        public const string UseSsl = "useSsl";

        /// <summary>Host name expected on the server certificate (SNI). Defaults to the endpoint host.</summary>
        public const string SslHost = "sslHost";

        /// <summary>Whether to verify the server certificate.</summary>
        public const string VerifyTls = "verifyTls";

        /// <summary>PEM certificate of a private certificate authority to trust for TLS.</summary>
        public const string CaCertificate = "caCertificate";

        /// <summary>Database index.</summary>
        public const string Database = "database";

        /// <summary>ACL user name.</summary>
        public const string Username = "username";

        /// <summary>Topology: <c>Auto</c>, <c>Standalone</c>, <c>Cluster</c>, or <c>Sentinel</c>.</summary>
        public const string ClusterMode = "clusterMode";

        /// <summary>Sentinel service (primary set) name.</summary>
        public const string SentinelServiceName = "sentinelServiceName";

        /// <summary>Most keys one scan may examine.</summary>
        public const string MaxKeysScanned = "maxKeysScanned";

        /// <summary>Largest value read or written, in bytes.</summary>
        public const string MaxValueBytes = "maxValueBytes";

        /// <summary>Connect timeout in seconds.</summary>
        public const string ConnectTimeoutSeconds = "connectTimeoutSeconds";

        /// <summary>Command timeout in seconds enforced by the client library.</summary>
        public const string CommandTimeoutSeconds = "commandTimeoutSeconds";

        /// <summary>Commands rejected by <c>redis_command</c>, one per line.</summary>
        public const string DeniedCommands = "deniedCommands";

        /// <summary>Extra commands (for example module commands) classified as Read by <c>redis_command</c>.</summary>
        public const string ReadOnlyCommands = "readOnlyCommands";

        /// <summary>Client name reported to the server.</summary>
        public const string ClientName = "clientName";

        /// <summary>Secret slot holding the password.</summary>
        public const string PasswordSlot = "password";

        /// <summary>Field of the password secret.</summary>
        public const string PasswordField = "password";

        /// <summary>Optional user name field of the password secret.</summary>
        public const string UsernameField = "username";

        /// <summary>Cluster mode value: detect from the server.</summary>
        public const string ModeAuto = "Auto";

        /// <summary>Cluster mode value: a single primary (with optional replicas).</summary>
        public const string ModeStandalone = "Standalone";

        /// <summary>Cluster mode value: Redis Cluster.</summary>
        public const string ModeCluster = "Cluster";

        /// <summary>Cluster mode value: discover the primary through Sentinel.</summary>
        public const string ModeSentinel = "Sentinel";
    }
}
