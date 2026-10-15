namespace Hugo.Connectors.Docker
{
    /// <summary>
    /// Setting keys, secret slot names, and secret field names of the <c>docker</c> connector. These are part of the
    /// resource contract and are not configurable. Thread-safe.
    /// </summary>
    public static class DockerSettingKeys
    {
        /// <summary>Daemon endpoint: <c>unix:///var/run/docker.sock</c>, <c>tcp://host:2376</c>, or <c>npipe://./pipe/docker_engine</c>.</summary>
        public const string Endpoint = "endpoint";

        /// <summary>Whether to verify the daemon's TLS certificate.</summary>
        public const string TlsVerify = "tlsVerify";

        /// <summary>Per-request timeout in seconds.</summary>
        public const string RequestTimeoutSeconds = "requestTimeoutSeconds";

        /// <summary>Most log lines returned by <c>docker_logs</c>.</summary>
        public const string MaxLogLines = "maxLogLines";

        /// <summary>Whether <c>docker_inspect</c> shows environment variable values.</summary>
        public const string ShowEnvironmentValues = "showEnvironmentValues";

        /// <summary>Largest time window <c>docker_events</c> reads, in seconds.</summary>
        public const string MaxEventsWindowSeconds = "maxEventsWindowSeconds";

        /// <summary>Default and maximum run time of <c>docker_exec</c> in seconds.</summary>
        public const string ExecTimeoutSeconds = "execTimeoutSeconds";

        /// <summary>Most containers <c>docker_stats</c> samples in one call.</summary>
        public const string MaxStatsContainers = "maxStatsContainers";

        /// <summary>Secret slot holding a Certificate secret for TLS client authentication.</summary>
        public const string TlsClientCertificateSlot = "tlsClientCertificate";

        /// <summary>Field of a Certificate secret holding the PEM client certificate.</summary>
        public const string CertificateField = "certificate";

        /// <summary>Field of a Certificate secret holding the PEM private key.</summary>
        public const string PrivateKeyField = "privateKey";

        /// <summary>Field of a Certificate secret holding the PEM CA certificate that signed the daemon's certificate.</summary>
        public const string CaCertificateField = "caCertificate";
    }
}
