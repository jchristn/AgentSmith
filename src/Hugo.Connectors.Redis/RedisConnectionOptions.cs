namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// Typed view of a <c>redis</c> resource's settings, with defaults applied and ranges clamped. Builds the
    /// StackExchange.Redis <see cref="ConfigurationOptions"/>. Not thread-safe for mutation.
    /// </summary>
    public class RedisConnectionOptions
    {
        #region Public-Members

        /// <summary>
        /// Default Redis port.
        /// </summary>
        public static readonly int DefaultPort = 6379;

        /// <summary>
        /// Default Sentinel port.
        /// </summary>
        public static readonly int DefaultSentinelPort = 26379;

        /// <summary>
        /// Endpoints (Sentinels in Sentinel mode). Never null; may be empty when the setting is invalid.
        /// </summary>
        public List<RedisEndpoint> Endpoints { get; set; } = new List<RedisEndpoint>();

        /// <summary>
        /// Whether to connect with TLS. Default false.
        /// </summary>
        public bool UseSsl { get; set; } = false;

        /// <summary>
        /// Host name expected on the certificate, or null for the endpoint host.
        /// </summary>
        public string? SslHost { get; set; } = null;

        /// <summary>
        /// Whether to verify the server certificate. Default true.
        /// </summary>
        public bool VerifyTls { get; set; } = true;

        /// <summary>
        /// PEM of a private certificate authority to trust, or null.
        /// </summary>
        public string? CaCertificatePem { get; set; } = null;

        /// <summary>
        /// Database index. Minimum 0, maximum 10000, default 0. Redis Cluster supports only database 0.
        /// </summary>
        public int Database
        {
            get
            {
                return _Database;
            }
            set
            {
                _Database = Math.Clamp(value, 0, 10000);
            }
        }

        /// <summary>
        /// ACL user name, or null for the default user.
        /// </summary>
        public string? Username { get; set; } = null;

        /// <summary>
        /// Topology. Default Auto.
        /// </summary>
        public RedisClusterModeEnum ClusterMode { get; set; } = RedisClusterModeEnum.Auto;

        /// <summary>
        /// Sentinel service name, required in Sentinel mode.
        /// </summary>
        public string? SentinelServiceName { get; set; } = null;

        /// <summary>
        /// Most keys one scan examines. Minimum 100, maximum 1000000, default 10000.
        /// </summary>
        public int MaxKeysScanned
        {
            get
            {
                return _MaxKeysScanned;
            }
            set
            {
                _MaxKeysScanned = Math.Clamp(value, 100, 1000000);
            }
        }

        /// <summary>
        /// Largest value read or written, in bytes. Longer values are truncated on read and rejected on write. Minimum 64,
        /// maximum 16777216, default 65536.
        /// </summary>
        public int MaxValueBytes
        {
            get
            {
                return _MaxValueBytes;
            }
            set
            {
                _MaxValueBytes = Math.Clamp(value, 64, 16 * 1024 * 1024);
            }
        }

        /// <summary>
        /// Connect timeout in seconds. Minimum 1, maximum 60, default 5.
        /// </summary>
        public int ConnectTimeoutSeconds
        {
            get
            {
                return _ConnectTimeoutSeconds;
            }
            set
            {
                _ConnectTimeoutSeconds = Math.Clamp(value, 1, 60);
            }
        }

        /// <summary>
        /// Command timeout in seconds enforced by the client library, as a backstop to the call timeout. Minimum 1,
        /// maximum 600, default 30.
        /// </summary>
        public int CommandTimeoutSeconds
        {
            get
            {
                return _CommandTimeoutSeconds;
            }
            set
            {
                _CommandTimeoutSeconds = Math.Clamp(value, 1, 600);
            }
        }

        /// <summary>
        /// Commands rejected by <c>redis_command</c> (upper case). Default contains KEYS.
        /// </summary>
        public List<string> DeniedCommands { get; set; } = new List<string> { "KEYS" };

        /// <summary>
        /// Extra commands classified as Read by <c>redis_command</c> (upper case). They never override a built-in
        /// classification. Default empty.
        /// </summary>
        public List<string> ReadOnlyCommands { get; set; } = new List<string>();

        /// <summary>
        /// Client name reported to the server. Default <c>hugo</c>.
        /// </summary>
        public string ClientName { get; set; } = "hugo";

        #endregion

        #region Private-Members

        private int _Database = 0;
        private int _MaxKeysScanned = 10000;
        private int _MaxValueBytes = 65536;
        private int _ConnectTimeoutSeconds = 5;
        private int _CommandTimeoutSeconds = 30;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read options from resource settings. Invalid endpoints are skipped (validation reports them).
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static RedisConnectionOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            RedisConnectionOptions o = new RedisConnectionOptions();
            o.ClusterMode = ParseMode(settings.GetString(RedisSettingKeys.ClusterMode));
            int defaultPort = o.ClusterMode == RedisClusterModeEnum.Sentinel ? DefaultSentinelPort : DefaultPort;
            foreach (string entry in SplitEndpoints(settings))
            {
                if (RedisEndpoint.TryParse(entry, defaultPort, out RedisEndpoint? ep, out string _) && ep != null) o.Endpoints.Add(ep);
            }
            o.UseSsl = settings.GetBool(RedisSettingKeys.UseSsl, false);
            o.SslHost = settings.GetString(RedisSettingKeys.SslHost);
            o.VerifyTls = settings.GetBool(RedisSettingKeys.VerifyTls, true);
            o.CaCertificatePem = settings.GetString(RedisSettingKeys.CaCertificate);
            o.Database = settings.GetInt(RedisSettingKeys.Database, 0, 0, 10000);
            o.Username = settings.GetString(RedisSettingKeys.Username);
            o.SentinelServiceName = settings.GetString(RedisSettingKeys.SentinelServiceName);
            o.MaxKeysScanned = settings.GetInt(RedisSettingKeys.MaxKeysScanned, 10000, 100, 1000000);
            o.MaxValueBytes = settings.GetInt(RedisSettingKeys.MaxValueBytes, 65536, 64, 16 * 1024 * 1024);
            o.ConnectTimeoutSeconds = settings.GetInt(RedisSettingKeys.ConnectTimeoutSeconds, 5, 1, 60);
            o.CommandTimeoutSeconds = settings.GetInt(RedisSettingKeys.CommandTimeoutSeconds, 30, 1, 600);
            if (settings.Has(RedisSettingKeys.DeniedCommands))
                o.DeniedCommands = settings.GetStringList(RedisSettingKeys.DeniedCommands).Select(c => c.ToUpperInvariant()).ToList();
            o.ReadOnlyCommands = settings.GetStringList(RedisSettingKeys.ReadOnlyCommands).Select(c => c.ToUpperInvariant()).ToList();
            o.ClientName = settings.GetString(RedisSettingKeys.ClientName, "hugo") ?? "hugo";
            return o;
        }

        /// <summary>
        /// Endpoint entries of the setting: one per line, and commas also separate entries.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Entries.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static List<string> SplitEndpoints(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            List<string> entries = new List<string>();
            foreach (string line in settings.GetStringList(RedisSettingKeys.Endpoints))
            {
                foreach (string part in line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) entries.Add(part);
            }
            return entries;
        }

        /// <summary>
        /// Parse a cluster mode value (case-insensitive). Unknown or empty values mean Auto.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Mode.</returns>
        public static RedisClusterModeEnum ParseMode(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return RedisClusterModeEnum.Auto;
            if (Enum.TryParse<RedisClusterModeEnum>(value.Trim(), true, out RedisClusterModeEnum mode) && Enum.IsDefined(typeof(RedisClusterModeEnum), mode)) return mode;
            return RedisClusterModeEnum.Auto;
        }

        /// <summary>
        /// Build the StackExchange.Redis configuration. The password is copied into the configuration, which lives only
        /// as long as the pooled connection.
        /// </summary>
        /// <param name="password">Password, or null.</param>
        /// <param name="secretUsername">User name stored with the password secret, used when the username setting is empty.</param>
        /// <returns>Configuration.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when no endpoint is configured, Sentinel mode
        /// lacks a service name, or the CA certificate cannot be read.</exception>
        public ConfigurationOptions BuildConfiguration(string? password, string? secretUsername)
        {
            if (!Endpoints.Any()) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The endpoints setting has no valid host:port entry.");
            if (ClusterMode == RedisClusterModeEnum.Sentinel && String.IsNullOrWhiteSpace(SentinelServiceName))
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "clusterMode is Sentinel, so sentinelServiceName is required.");

            ConfigurationOptions config = new ConfigurationOptions();
            foreach (RedisEndpoint ep in Endpoints) config.EndPoints.Add(ep.Host, ep.Port);
            config.AbortOnConnectFail = true;
            config.AllowAdmin = true;
            config.ConnectRetry = 1;
            config.ConnectTimeout = ConnectTimeoutSeconds * 1000;
            config.SyncTimeout = CommandTimeoutSeconds * 1000;
            config.AsyncTimeout = CommandTimeoutSeconds * 1000;
            config.DefaultDatabase = Database;
            config.ClientName = ClientName;
            config.KeepAlive = 30;
            config.IncludeDetailInExceptions = true;
            config.Protocol = RedisProtocol.Resp2;

            string? user = !String.IsNullOrWhiteSpace(Username) ? Username : (String.IsNullOrWhiteSpace(secretUsername) ? null : secretUsername);
            if (user != null) config.User = user;
            if (!String.IsNullOrEmpty(password)) config.Password = password;

            if (ClusterMode == RedisClusterModeEnum.Sentinel)
            {
                config.ServiceName = SentinelServiceName;
                if (user != null) config.SentinelUser = user;
                if (!String.IsNullOrEmpty(password)) config.SentinelPassword = password;
            }

            if (UseSsl)
            {
                config.Ssl = true;
                if (!String.IsNullOrWhiteSpace(SslHost)) config.SslHost = SslHost;
                if (!VerifyTls)
                {
                    config.CertificateValidation += (sender, certificate, chain, errors) => true;
                }
                else if (!String.IsNullOrWhiteSpace(CaCertificatePem))
                {
                    X509Certificate2 ca;
                    try
                    {
                        ca = X509Certificate2.CreateFromPem(CaCertificatePem);
                    }
                    catch (CryptographicException e)
                    {
                        throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "caCertificate is not a readable PEM certificate: " + e.Message, e);
                    }
                    config.TrustIssuer(ca);
                }
            }

            return config;
        }

        /// <summary>
        /// Display form of the endpoints, comma separated.
        /// </summary>
        /// <returns>Text.</returns>
        public string EndpointsText()
        {
            return String.Join(",", Endpoints.Select(e => e.ToString()));
        }

        #endregion
    }
}
