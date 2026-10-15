namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Redis.Tools;
    using StackExchange.Redis;

    /// <summary>
    /// Redis connector (type <c>redis</c>, family <c>redis</c>): server diagnostics, SCAN-based key discovery, type-aware
    /// reads, guarded writes and deletes, and a classified command escape hatch, over standalone, Sentinel, or Cluster
    /// deployments with optional TLS. Built on StackExchange.Redis. Thread-safe.
    /// </summary>
    public class RedisConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "redis";

        /// <summary>
        /// Tool family.
        /// </summary>
        public static readonly string FamilyName = "redis";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        #endregion

        #region Private-Members

        private static readonly Regex _CommandName = new Regex("^[A-Za-z][A-Za-z0-9._|-]{0,63}( [A-Za-z][A-Za-z0-9._|-]{0,63})?$", RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new RedisInfoTool(),
                new RedisScanTool(),
                new RedisGetTool(),
                new RedisTtlTool(),
                new RedisMemoryUsageTool(),
                new RedisSlowlogTool(),
                new RedisClientListTool(),
                new RedisLatencyTool(),
                new RedisBigKeysTool(),
                new RedisSetTool(),
                new RedisExpireTool(),
                new RedisDeleteTool(),
                new RedisCommandTool()
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

            RedisClusterModeEnum mode = RedisClusterModeEnum.Auto;
            string? modeText = s.GetString(RedisSettingKeys.ClusterMode);
            if (!String.IsNullOrWhiteSpace(modeText))
            {
                if (!Enum.TryParse<RedisClusterModeEnum>(modeText.Trim(), true, out mode) || !Enum.IsDefined(typeof(RedisClusterModeEnum), mode))
                {
                    result.Add(RedisSettingKeys.ClusterMode, "invalid_value", "clusterMode must be Auto, Standalone, Cluster, or Sentinel.");
                    mode = RedisClusterModeEnum.Auto;
                }
            }

            List<string> endpoints = RedisConnectionOptions.SplitEndpoints(s);
            if (!endpoints.Any())
            {
                result.Add(RedisSettingKeys.Endpoints, "required", "Add at least one endpoint as host:port, for example 10.0.0.5:6379.");
            }
            else
            {
                int defaultPort = mode == RedisClusterModeEnum.Sentinel ? RedisConnectionOptions.DefaultSentinelPort : RedisConnectionOptions.DefaultPort;
                foreach (string e in endpoints)
                {
                    if (!RedisEndpoint.TryParse(e, defaultPort, out RedisEndpoint? _, out string error))
                    {
                        result.Add(RedisSettingKeys.Endpoints, "invalid_format", error);
                        break;
                    }
                }
                if (mode == RedisClusterModeEnum.Standalone && endpoints.Count > 1)
                    result.Add(RedisSettingKeys.Endpoints, "invalid_value", "clusterMode is Standalone, so give exactly one endpoint (the primary).");
            }

            if (mode == RedisClusterModeEnum.Sentinel && String.IsNullOrWhiteSpace(s.GetString(RedisSettingKeys.SentinelServiceName)))
                result.Add(RedisSettingKeys.SentinelServiceName, "required", "clusterMode is Sentinel, so set the Sentinel service name (the primary set name, for example mymaster).");
            if (mode != RedisClusterModeEnum.Sentinel && mode != RedisClusterModeEnum.Auto && s.Has(RedisSettingKeys.SentinelServiceName))
                result.Add(RedisSettingKeys.SentinelServiceName, "invalid_value", "sentinelServiceName is only used when clusterMode is Sentinel.");

            ValidateRange(s, result, RedisSettingKeys.Database, 0, 10000);
            if (mode == RedisClusterModeEnum.Cluster && s.GetLong(RedisSettingKeys.Database, 0) != 0)
                result.Add(RedisSettingKeys.Database, "invalid_value", "Redis Cluster supports only database 0.");
            ValidateRange(s, result, RedisSettingKeys.MaxKeysScanned, 100, 1000000);
            ValidateRange(s, result, RedisSettingKeys.MaxValueBytes, 64, 16 * 1024 * 1024);
            ValidateRange(s, result, RedisSettingKeys.ConnectTimeoutSeconds, 1, 60);
            ValidateRange(s, result, RedisSettingKeys.CommandTimeoutSeconds, 1, 600);

            string? user = s.GetString(RedisSettingKeys.Username);
            if (user != null && user.IndexOfAny(new char[] { ' ', '\t', '\n', '\r', '\0' }) >= 0)
                result.Add(RedisSettingKeys.Username, "invalid_format", "The username must not contain whitespace.");

            string? ca = s.GetString(RedisSettingKeys.CaCertificate);
            if (!String.IsNullOrWhiteSpace(ca))
            {
                if (!s.GetBool(RedisSettingKeys.UseSsl, false)) result.Add(RedisSettingKeys.CaCertificate, "invalid_value", "caCertificate is only used when useSsl is true.");
                try
                {
                    using (X509Certificate2 cert = X509Certificate2.CreateFromPem(ca))
                    {
                    }
                }
                catch (CryptographicException e)
                {
                    result.Add(RedisSettingKeys.CaCertificate, "invalid_format", "caCertificate is not a readable PEM certificate: " + e.Message);
                }
            }

            ValidateCommands(s, result, RedisSettingKeys.DeniedCommands);
            ValidateCommands(s, result, RedisSettingKeys.ReadOnlyCommands);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                using (RedisSession session = await RedisSessionFactory.ConnectAsync(resource, token).ConfigureAwait(false))
                {
                    IServer server = session.Primaries()[0];
                    TimeSpan ping = await server.PingAsync(CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                    string info = (await server.InfoRawAsync("server", CommandFlags.None).WaitAsync(token).ConfigureAwait(false)) ?? String.Empty;
                    sw.Stop();

                    string version = Field(info, "redis_version") ?? server.Version.ToString();
                    string mode = session.IsCluster ? "cluster" : (session.Options.ClusterMode == RedisClusterModeEnum.Sentinel ? "sentinel" : (Field(info, "redis_mode") ?? "standalone"));
                    ConnectorHealthResult ok = ConnectorHealthResult.Ok(
                        "Connected to Redis " + version + " (" + mode + ") at " + RedisSession.Format(server) + ", database " + session.DatabaseIndex().ToString(CultureInfo.InvariantCulture) + ".",
                        sw.Elapsed.TotalMilliseconds);
                    ok.Details["version"] = version;
                    ok.Details["mode"] = mode;
                    ok.Details["primaries"] = session.Primaries().Count.ToString(CultureInfo.InvariantCulture);
                    ok.Details["database"] = session.DatabaseIndex().ToString(CultureInfo.InvariantCulture);
                    ok.Details["tls"] = session.Options.UseSsl ? "true" : "false";
                    ok.Details["pingMs"] = ping.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
                    if (session.Options.ClusterMode == RedisClusterModeEnum.Cluster && !session.IsCluster)
                        ok.Details["warning"] = "clusterMode is Cluster, but the server is not a Redis Cluster.";
                    if (session.Options.ClusterMode == RedisClusterModeEnum.Standalone && session.IsCluster)
                        ok.Details["warning"] = "clusterMode is Standalone, but the server is a Redis Cluster.";
                    return ok;
                }
            }
            catch (ConnectorException e)
            {
                return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "The Redis connection test did not finish in time.", sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception e) when (e is RedisException || e is System.Net.Sockets.SocketException || e is System.IO.IOException)
            {
                ConnectorException mapped = RedisErrorMapper.Map(e, "connection test");
                return ConnectorHealthResult.Fail(mapped.Code, mapped.Message, sw.Elapsed.TotalMilliseconds);
            }
        }

        #endregion

        #region Private-Methods

        private static string? Field(string info, string name)
        {
            foreach (string line in info.Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (l.StartsWith(name + ":", StringComparison.Ordinal)) return l.Substring(name.Length + 1);
            }
            return null;
        }

        private static void ValidateRange(ResourceSettings s, ConnectorValidationResult result, string key, long min, long max)
        {
            string? raw = s.GetString(key);
            if (raw == null) return;
            if (!Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < min || value > max)
                result.Add(key, "out_of_range", key + " must be an integer from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void ValidateCommands(ResourceSettings s, ConnectorValidationResult result, string key)
        {
            foreach (string entry in s.GetStringList(key))
            {
                string normalized = String.Join(" ", entry.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                if (!_CommandName.IsMatch(normalized))
                {
                    result.Add(key, "invalid_format", "Entry '" + entry + "' must be a command name, optionally followed by one subcommand (for example KEYS or CONFIG SET).");
                    return;
                }
            }
        }

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor(RedisSettingKeys.Endpoints, SettingValueKindEnum.StringList, "Endpoints") { Required = true, Group = "Connection", ShowInSummary = true, Help = "One host:port per line (IPv6 as [address]:port). Standalone: the primary. Cluster: one or more seed nodes. Sentinel: the Sentinel nodes (default port 26379). Default port 6379." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.ClusterMode, SettingValueKindEnum.Enum, "Topology") { DefaultValue = RedisSettingKeys.ModeAuto, AllowedValues = new List<string> { RedisSettingKeys.ModeAuto, RedisSettingKeys.ModeStandalone, RedisSettingKeys.ModeCluster, RedisSettingKeys.ModeSentinel }, Group = "Connection", Help = "Auto detects standalone or cluster. Sentinel resolves the current primary of sentinelServiceName through the Sentinel endpoints." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.SentinelServiceName, SettingValueKindEnum.String, "Sentinel service name") { Group = "Connection", Help = "Primary set name monitored by Sentinel (for example mymaster). Required when the topology is Sentinel." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.Database, SettingValueKindEnum.Integer, "Database") { DefaultValue = "0", Minimum = 0, Maximum = 10000, Group = "Connection", ShowInSummary = true, Help = "Database index. Default 0. Redis Cluster only has database 0." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.Username, SettingValueKindEnum.String, "ACL username") { Group = "Connection", Help = "Redis 6+ ACL user. Empty uses the default user (password only). A dedicated user with only the commands Hugo needs is recommended." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.UseSsl, SettingValueKindEnum.Boolean, "Use TLS") { DefaultValue = "false", Group = "TLS", Help = "Connect with TLS (rediss). Required by most managed Redis services." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.SslHost, SettingValueKindEnum.String, "TLS host name") { Group = "TLS", Help = "Host name expected on the server certificate when it differs from the endpoint host." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.VerifyTls, SettingValueKindEnum.Boolean, "Verify TLS certificate") { DefaultValue = "true", Group = "TLS", Help = "Verify the server certificate. Turn off only for test servers with self-signed certificates." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.CaCertificate, SettingValueKindEnum.Text, "CA certificate (PEM)") { Group = "TLS", Help = "PEM certificate of a private certificate authority that signed the server certificate." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.ConnectTimeoutSeconds, SettingValueKindEnum.Integer, "Connect timeout (seconds)") { DefaultValue = "5", Minimum = 1, Maximum = 60, Group = "Connection", Help = "How long to wait for a connection. Default 5." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.CommandTimeoutSeconds, SettingValueKindEnum.Integer, "Command timeout (seconds)") { DefaultValue = "30", Minimum = 1, Maximum = 600, Group = "Connection", Help = "Client-side limit on one command, a backstop to the tool call timeout. Default 30." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.ClientName, SettingValueKindEnum.String, "Client name") { DefaultValue = "hugo", Group = "Connection", Help = "Name shown in CLIENT LIST for Hugo's connection. Default hugo." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.MaxKeysScanned, SettingValueKindEnum.Integer, "Max keys scanned") { DefaultValue = "10000", Minimum = 100, Maximum = 1000000, Group = "Limits", Help = "Most keys one redis_scan or redis_bigkeys call examines before it stops and returns a cursor. Default 10000." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.MaxValueBytes, SettingValueKindEnum.Integer, "Max value bytes") { DefaultValue = "65536", Minimum = 64, Maximum = 16 * 1024 * 1024, Group = "Limits", Help = "Values longer than this are cut when read, and redis_set refuses larger values. Default 65536." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.DeniedCommands, SettingValueKindEnum.StringList, "Denied commands") { DefaultValue = "KEYS", Group = "Safety", Help = "Commands redis_command rejects outright, one per line, optionally with a subcommand (for example CONFIG SET). Default KEYS, which blocks the server while it walks every key." });
            schema.Add(new SettingDescriptor(RedisSettingKeys.ReadOnlyCommands, SettingValueKindEnum.StringList, "Extra read-only commands") { Group = "Safety", Help = "Module or custom commands that only read (for example JSON.GET or FT.SEARCH), one per line. They are classified as Read instead of Destructive. Built-in commands keep their classification." });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = TypeName;
            d.Family = FamilyName;
            d.DisplayName = "Redis";
            d.Description = "Redis and Redis-compatible caches (standalone, Sentinel, Cluster, TLS): server and memory diagnostics, slow log, latency, client list, SCAN-based key discovery, type-aware reads, sampled big key analysis, guarded writes and deletes, and a classified command escape hatch.";
            d.Category = ConnectorCategoryEnum.Cache;
            d.IconKey = "cache";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(RedisSettingKeys.PasswordSlot, "Password", SecretKindEnum.Password) { Help = "Redis password (field 'password'; an optional 'username' field is used when the username setting is empty). Leave unbound for servers without authentication." }
            };
            d.DefaultInstructionsKey = "connector." + TypeName + ".instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(RedisConnector).Assembly, "connector.redis.instructions.md");
            d.DocumentationUrl = "https://redis.io/docs/latest/commands/";
            return d;
        }

        #endregion
    }
}
