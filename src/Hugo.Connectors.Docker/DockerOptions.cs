namespace Hugo.Connectors.Docker
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed snapshot of a Docker resource's settings, with defaults and range clamping applied. Not thread-safe for
    /// mutation; build one per call with <see cref="FromSettings"/>.
    /// </summary>
    public class DockerOptions
    {
        #region Public-Members

        /// <summary>
        /// Daemon endpoint. Default <c>unix:///var/run/docker.sock</c>.
        /// </summary>
        public string Endpoint
        {
            get
            {
                return _Endpoint;
            }
            set
            {
                _Endpoint = String.IsNullOrWhiteSpace(value) ? "unix:///var/run/docker.sock" : value.Trim();
            }
        }

        /// <summary>
        /// Whether the daemon's TLS certificate is verified. Default false. When true, a client certificate must be bound.
        /// </summary>
        public bool TlsVerify { get; set; } = false;

        /// <summary>
        /// Per-request timeout in seconds. Minimum 1, maximum 600, default 30.
        /// </summary>
        public int RequestTimeoutSeconds
        {
            get
            {
                return _RequestTimeoutSeconds;
            }
            set
            {
                _RequestTimeoutSeconds = Math.Clamp(value, 1, 600);
            }
        }

        /// <summary>
        /// Most log lines returned by <c>docker_logs</c>. Minimum 1, maximum 100000, default 500.
        /// </summary>
        public int MaxLogLines
        {
            get
            {
                return _MaxLogLines;
            }
            set
            {
                _MaxLogLines = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Whether <c>docker_inspect</c> shows environment variable values. Default false (values are redacted, names
        /// stay visible).
        /// </summary>
        public bool ShowEnvironmentValues { get; set; } = false;

        /// <summary>
        /// Largest time window <c>docker_events</c> reads, in seconds. Minimum 60, maximum 604800, default 3600.
        /// </summary>
        public int MaxEventsWindowSeconds
        {
            get
            {
                return _MaxEventsWindowSeconds;
            }
            set
            {
                _MaxEventsWindowSeconds = Math.Clamp(value, 60, 604800);
            }
        }

        /// <summary>
        /// Default and maximum run time of <c>docker_exec</c> in seconds. Minimum 1, maximum 900, default 60.
        /// </summary>
        public int ExecTimeoutSeconds
        {
            get
            {
                return _ExecTimeoutSeconds;
            }
            set
            {
                _ExecTimeoutSeconds = Math.Clamp(value, 1, 900);
            }
        }

        /// <summary>
        /// Most containers <c>docker_stats</c> samples in one call. Minimum 1, maximum 100, default 20.
        /// </summary>
        public int MaxStatsContainers
        {
            get
            {
                return _MaxStatsContainers;
            }
            set
            {
                _MaxStatsContainers = Math.Clamp(value, 1, 100);
            }
        }

        #endregion

        #region Private-Members

        private string _Endpoint = "unix:///var/run/docker.sock";
        private int _RequestTimeoutSeconds = 30;
        private int _MaxLogLines = 500;
        private int _MaxEventsWindowSeconds = 3600;
        private int _ExecTimeoutSeconds = 60;
        private int _MaxStatsContainers = 20;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build options from resource settings, applying defaults and clamping.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static DockerOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            DockerOptions o = new DockerOptions();
            o.Endpoint = settings.GetString(DockerSettingKeys.Endpoint, "unix:///var/run/docker.sock") ?? "unix:///var/run/docker.sock";
            o.TlsVerify = settings.GetBool(DockerSettingKeys.TlsVerify, false);
            o.RequestTimeoutSeconds = settings.GetInt(DockerSettingKeys.RequestTimeoutSeconds, 30, 1, 600);
            o.MaxLogLines = settings.GetInt(DockerSettingKeys.MaxLogLines, 500, 1, 100000);
            o.ShowEnvironmentValues = settings.GetBool(DockerSettingKeys.ShowEnvironmentValues, false);
            o.MaxEventsWindowSeconds = settings.GetInt(DockerSettingKeys.MaxEventsWindowSeconds, 3600, 60, 604800);
            o.ExecTimeoutSeconds = settings.GetInt(DockerSettingKeys.ExecTimeoutSeconds, 60, 1, 900);
            o.MaxStatsContainers = settings.GetInt(DockerSettingKeys.MaxStatsContainers, 20, 1, 100);
            return o;
        }

        #endregion
    }
}
