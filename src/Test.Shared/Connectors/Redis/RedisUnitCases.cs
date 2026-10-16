namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Redis;
    using Hugo.Connectors.Redis.Classification;
    using Hugo.Core.Connectors;
    using StackExchange.Redis;
    using Touchstone.Core;

    /// <summary>
    /// Redis connector unit cases that need no Redis server: descriptor, schemas, validation, options, classification,
    /// rendering, error mapping, and failure behavior against closed or silent local ports.
    /// </summary>
    public static class RedisUnitCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "connectors.redis";

        #endregion

        #region Private-Members

        private static readonly string[] _ToolNames = new string[]
        {
            "redis_info", "redis_scan", "redis_get", "redis_ttl", "redis_memory_usage", "redis_slowlog", "redis_client_list",
            "redis_latency", "redis_bigkeys", "redis_set", "redis_expire", "redis_delete", "redis_command"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "descriptor", "Descriptor has type, family, category, icon, slot, and instructions", Descriptor);
            Add(cases, "settings-schema", "Settings schema lists every documented key with defaults and ranges", SettingsSchema);
            Add(cases, "tools", "Tool set, names, base effects, and per-call flags", ToolsShape);
            Add(cases, "tool-schemas", "Every input schema parses, closes additional properties, and describes every property", ToolSchemas);
            Add(cases, "family-schemas", "Same-named tools in the redis family must share schemas (registry rule)", FamilySchemas);
            Add(cases, "validate-ok", "Valid standalone, cluster, and sentinel settings pass", ValidateOk);
            Add(cases, "validate-errors", "Bad endpoints, topology, ranges, CA, and command lists are reported per field", ValidateErrors);
            Add(cases, "endpoint-parse", "Endpoints parse host, host:port, and [ipv6]:port and reject schemes", EndpointParse);
            Add(cases, "options-defaults", "Options apply defaults and clamp ranges", OptionsDefaults);
            Add(cases, "options-configuration", "Configuration carries endpoints, auth, TLS, sentinel, and admin flags", OptionsConfiguration);
            Add(cases, "tokenizer", "Command lines tokenize with quotes and escapes", Tokenizer);
            Add(cases, "command-table", "The command table covers the Redis 7 command list", CommandTable);
            Add(cases, "classify-tools", "Fixed-effect tools classify with useful approval summaries", ClassifyTools);
            Add(cases, "classify-command", "redis_command classifies per call with resource options and summaries", ClassifyCommand);
            Add(cases, "denied-commands-additive", "deniedCommands adds to the KEYS denial; only allowKeysCommand lifts it", DeniedCommandsAdditive);
            Add(cases, "deletes-always-approval", "redis_delete and DEL, UNLINK, GETDEL in redis_command always require approval", DeletesAlwaysApproval);
            Add(cases, "display-values", "Values render as text or base64 and truncate on UTF-8 boundaries", DisplayValues);
            Add(cases, "result-renderer", "Raw replies render like redis-cli with element caps, and as JSON", ResultRenderer);
            Add(cases, "scan-cursor", "Scan cursors format and parse, and keys round trip through base64", ScanCursor);
            Add(cases, "error-mapping", "Server error replies map to stable codes", ErrorMapping);
            Add(cases, "arguments-before-connect", "Argument errors are reported without connecting", ArgumentsBeforeConnect);
            Add(cases, "read-only-defense", "Write and destructive calls refuse a ReadOnly binding", ReadOnlyDefense);
            Add(cases, "connection-refused", "A refused connection maps to connection_failed and TestConnection fails cleanly", ConnectionRefused);
            Add(cases, "timeout", "A server that never answers ends with a timeout error", Timeout);
            Add(cases, "cancellation", "Caller cancellation propagates as OperationCanceledException", Cancellation);
            Add(cases, "secret-not-echoed", "The password never appears in results or health messages", SecretNotEchoed);
            return cases;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
        }

        private static Dictionary<string, string> Closed()
        {
            return new Dictionary<string, string> { { "endpoints", "127.0.0.1:1" }, { "connectTimeoutSeconds", "2" } };
        }

        private static Task Descriptor(CancellationToken token)
        {
            ConnectorDescriptor d = RedisTestHelper.Connector.Descriptor;
            RedisTestHelper.Assert(d.Type == "redis" && d.Family == "redis", "type and family");
            RedisTestHelper.Assert(d.Category == ConnectorCategoryEnum.Cache && d.IconKey == "cache", "category and icon");
            RedisTestHelper.Assert(d.SecretSlots.Count == 1 && d.SecretSlots[0].Name == "password" && d.SecretSlots[0].AcceptedKinds.Contains(SecretKindEnum.Password) && !d.SecretSlots[0].Required, "password slot");
            RedisTestHelper.Assert(d.DefaultInstructionsKey == "connector.redis.instructions", "instructions key");
            RedisTestHelper.Assert(d.DefaultInstructions.Contains("redis_scan", StringComparison.Ordinal) && d.DefaultInstructions.Contains("KEYS", StringComparison.Ordinal), "instructions mention scan and KEYS");
            RedisTestHelper.Assert(!d.DefaultInstructions.Contains((char)0x2014) && !d.Description.Contains((char)0x2014), "no em-dashes");
            return Task.CompletedTask;
        }

        private static Task SettingsSchema(CancellationToken token)
        {
            ConnectorSettingsSchema s = RedisTestHelper.Connector.Descriptor.SettingsSchema;
            string[] keys = new string[] { "endpoints", "useSsl", "database", "username", "clusterMode", "sentinelServiceName", "maxKeysScanned", "maxValueBytes", "sslHost", "verifyTls", "caCertificate", "connectTimeoutSeconds", "commandTimeoutSeconds", "deniedCommands", "allowKeysCommand", "readOnlyCommands", "clientName" };
            foreach (string k in keys) RedisTestHelper.Assert(s.Find(k) != null, "setting " + k);
            RedisTestHelper.Assert(s.Find("endpoints")!.Required && s.Find("endpoints")!.Kind == SettingValueKindEnum.StringList, "endpoints required list");
            RedisTestHelper.Assert(s.Find("database")!.DefaultValue == "0", "database default 0");
            RedisTestHelper.Assert(s.Find("clusterMode")!.DefaultValue == "Auto" && s.Find("clusterMode")!.AllowedValues.Count == 4, "clusterMode enum");
            RedisTestHelper.Assert(s.Find("maxKeysScanned")!.DefaultValue == "10000" && s.Find("maxValueBytes")!.DefaultValue == "65536", "limits defaults");
            RedisTestHelper.Assert(s.Find("deniedCommands")!.DefaultValue == null, "deniedCommands holds only operator additions");
            RedisTestHelper.Assert(s.Find("allowKeysCommand")!.DefaultValue == "false" && s.Find("allowKeysCommand")!.Kind == SettingValueKindEnum.Boolean, "KEYS denied by default");
            RedisTestHelper.Assert(s.Settings.All(x => !String.IsNullOrEmpty(x.Help)), "every setting has help");
            return Task.CompletedTask;
        }

        private static Task ToolsShape(CancellationToken token)
        {
            List<string> names = RedisTestHelper.Connector.Tools.Select(t => t.Descriptor.Name).ToList();
            RedisTestHelper.Assert(names.SequenceEqual(_ToolNames), "tool names: " + String.Join(",", names));
            foreach (IConnectorTool t in RedisTestHelper.Connector.Tools)
            {
                EffectEnum expected = t.Descriptor.Name == "redis_set" || t.Descriptor.Name == "redis_expire" ? EffectEnum.Write
                    : t.Descriptor.Name == "redis_delete" ? EffectEnum.Destructive : EffectEnum.Read;
                RedisTestHelper.Assert(t.Descriptor.BaseEffect == expected, t.Descriptor.Name + " base effect");
                RedisTestHelper.Assert(t.Descriptor.ClassifiedPerCall == (t.Descriptor.Name == "redis_command"), t.Descriptor.Name + " per call flag");
                RedisTestHelper.Assert(t.Descriptor.Description.Length > 60 && !t.Descriptor.Description.Contains((char)0x2014), t.Descriptor.Name + " description");
                RedisTestHelper.Assert(!String.IsNullOrEmpty(t.Descriptor.Title) && !String.IsNullOrEmpty(t.Descriptor.OutputDescription), t.Descriptor.Name + " title and output");
            }
            return Task.CompletedTask;
        }

        private static Task ToolSchemas(CancellationToken token)
        {
            foreach (IConnectorTool t in RedisTestHelper.Connector.Tools)
            {
                using (JsonDocument doc = JsonDocument.Parse(t.Descriptor.InputSchemaJson))
                {
                    JsonElement root = doc.RootElement;
                    RedisTestHelper.Assert(root.GetProperty("type").GetString() == "object", t.Descriptor.Name + " object schema");
                    RedisTestHelper.Assert(root.GetProperty("additionalProperties").ValueKind == JsonValueKind.False, t.Descriptor.Name + " closed schema");
                    JsonElement props = root.GetProperty("properties");
                    RedisTestHelper.Assert(!props.TryGetProperty("resource", out JsonElement _), t.Descriptor.Name + " no resource argument");
                    foreach (JsonProperty p in props.EnumerateObject())
                        RedisTestHelper.Assert(p.Value.TryGetProperty("description", out JsonElement d) && d.GetString()!.Length > 10, t.Descriptor.Name + "." + p.Name + " described");
                    if (root.TryGetProperty("required", out JsonElement req))
                        foreach (JsonElement r in req.EnumerateArray()) RedisTestHelper.Assert(props.TryGetProperty(r.GetString()!, out JsonElement _), t.Descriptor.Name + " required property exists");
                }
            }
            return Task.CompletedTask;
        }

        private static Task FamilySchemas(CancellationToken token)
        {
            using (ConnectorRegistry registry = new ConnectorRegistry())
            {
                registry.Register(RedisTestHelper.Connector);
                registry.Register(new RedisFamilyProbeConnector("redis-probe", "redis", new List<IConnectorTool> { RedisTestHelper.Tool("redis_get"), RedisTestHelper.Tool("redis_scan") }));
                List<FamilyTool> family = registry.FamilyTools("redis");
                RedisTestHelper.Assert(family.Count == _ToolNames.Length + 2, "connector tools plus two shared resource context tools");
                FamilyTool get = family.First(f => f.Descriptor.Name == "redis_get");
                RedisTestHelper.Assert(get.ConnectorTypes.Contains("redis") && get.ConnectorTypes.Contains("redis-probe"), "shared tool lists both connectors");

                bool rejected = false;
                try
                {
                    registry.Register(new RedisFamilyProbeConnector("redis-bad", "redis", new List<IConnectorTool> { new RedisSchemaChangedTool("redis_get") }));
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
                RedisTestHelper.Assert(rejected, "a different schema for redis_get in the family is rejected");
            }
            return Task.CompletedTask;
        }

        private static async Task ValidateOk(CancellationToken token)
        {
            ConnectorValidationResult a = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "cache.internal:6379" } }, null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(a.IsValid, "standalone: " + String.Join(";", a.Errors.Select(e => e.Message)));
            ConnectorValidationResult b = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "10.0.0.1:7000\n10.0.0.2:7000,10.0.0.3" }, { "clusterMode", "Cluster" }, { "useSsl", "true" }, { "verifyTls", "false" } }, "pw"), token).ConfigureAwait(false);
            RedisTestHelper.Assert(b.IsValid, "cluster: " + String.Join(";", b.Errors.Select(e => e.Message)));
            ConnectorValidationResult c = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "s1\ns2:26380\n[::1]:26381" }, { "clusterMode", "sentinel" }, { "sentinelServiceName", "mymaster" }, { "deniedCommands", "KEYS\nCONFIG SET\nFLUSHALL" }, { "readOnlyCommands", "JSON.GET\nFT.SEARCH" } }, "pw"), token).ConfigureAwait(false);
            RedisTestHelper.Assert(c.IsValid, "sentinel: " + String.Join(";", c.Errors.Select(e => e.Message)));
        }

        private static async Task ValidateErrors(CancellationToken token)
        {
            ConnectorValidationResult none = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string>(), null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(none.Errors.Any(e => e.Field == "endpoints" && e.Code == "required"), "endpoints required");

            Dictionary<string, string> bad = new Dictionary<string, string>
            {
                { "endpoints", "redis://host:6379" },
                { "clusterMode", "Ring" },
                { "database", "-1" },
                { "maxKeysScanned", "5" },
                { "maxValueBytes", "1" },
                { "connectTimeoutSeconds", "0" },
                { "commandTimeoutSeconds", "9999" },
                { "username", "bad user" },
                { "caCertificate", "not a pem" },
                { "deniedCommands", "KEYS; FLUSHALL" },
                { "readOnlyCommands", "JSON GET EXTRA WORDS" }
            };
            ConnectorValidationResult r = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(bad, null), token).ConfigureAwait(false);
            HashSet<string> fields = new HashSet<string>(r.Errors.Select(e => e.Field));
            foreach (string f in new[] { "endpoints", "clusterMode", "database", "maxKeysScanned", "maxValueBytes", "connectTimeoutSeconds", "commandTimeoutSeconds", "username", "caCertificate", "deniedCommands", "readOnlyCommands" })
                RedisTestHelper.Assert(fields.Contains(f), "error on " + f + " (got " + String.Join(",", fields) + ")");

            ConnectorValidationResult s = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "s1" }, { "clusterMode", "Sentinel" } }, null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(s.Errors.Any(e => e.Field == "sentinelServiceName" && e.Code == "required"), "sentinel needs a service name");
            ConnectorValidationResult cl = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "a:1" }, { "clusterMode", "Cluster" }, { "database", "3" } }, null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(cl.Errors.Any(e => e.Field == "database"), "cluster only has database 0");
            ConnectorValidationResult st = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "a:1\nb:2" }, { "clusterMode", "Standalone" } }, null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(st.Errors.Any(e => e.Field == "endpoints"), "standalone takes one endpoint");
            ConnectorValidationResult ip6 = await RedisTestHelper.Connector.ValidateAsync(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "fe80::1:6379" } }, null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(ip6.Errors.Any(e => e.Field == "endpoints" && e.Message.Contains("[address]", StringComparison.Ordinal)), "unbracketed IPv6");
        }

        private static Task EndpointParse(CancellationToken token)
        {
            RedisTestHelper.Assert(RedisEndpoint.TryParse("cache", 6379, out RedisEndpoint? a, out string _) && a!.Host == "cache" && a.Port == 6379, "host only");
            RedisTestHelper.Assert(RedisEndpoint.TryParse(" cache:7000 ", 6379, out RedisEndpoint? b, out string _) && b!.Port == 7000, "host:port");
            RedisTestHelper.Assert(RedisEndpoint.TryParse("[::1]:6380", 6379, out RedisEndpoint? c, out string _) && c!.Host == "::1" && c.Port == 6380 && c.ToString() == "[::1]:6380", "ipv6");
            RedisTestHelper.Assert(RedisEndpoint.TryParse("[::1]", 26379, out RedisEndpoint? d, out string _) && d!.Port == 26379, "ipv6 default port");
            RedisTestHelper.Assert(!RedisEndpoint.TryParse("cache:0", 6379, out RedisEndpoint? _, out string e1) && e1.Contains("port", StringComparison.Ordinal), "port 0");
            RedisTestHelper.Assert(!RedisEndpoint.TryParse("cache:99999", 6379, out RedisEndpoint? _, out string _), "port too large");
            RedisTestHelper.Assert(!RedisEndpoint.TryParse("rediss://cache:6380", 6379, out RedisEndpoint? _, out string e2) && e2.Contains("useSsl", StringComparison.Ordinal), "scheme");
            RedisTestHelper.Assert(!RedisEndpoint.TryParse("", 6379, out RedisEndpoint? _, out string _), "empty");
            RedisTestHelper.Assert(!RedisEndpoint.TryParse("a b:1", 6379, out RedisEndpoint? _, out string _), "space");
            RedisTestHelper.Assert(!RedisEndpoint.TryParse("[::1", 6379, out RedisEndpoint? _, out string _), "unclosed bracket");
            return Task.CompletedTask;
        }

        private static Task DeniedCommandsAdditive(CancellationToken token)
        {
            ConnectorInvocationContext extra = RedisTestHelper.Context(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "h" }, { "deniedCommands", "FLUSHALL" } }, null));
            RedisTestHelper.Assert(RedisTestHelper.Classify("redis_command", "{\"command\":\"KEYS *\"}", extra).Rejected, "KEYS stays denied when the operator denies something else");
            RedisTestHelper.Assert(RedisTestHelper.Classify("redis_command", "{\"command\":\"FLUSHALL\"}", extra).Rejected, "the operator's denial applies");

            ConnectorInvocationContext empty = RedisTestHelper.Context(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "h" }, { "deniedCommands", "" } }, null));
            RedisTestHelper.Assert(RedisTestHelper.Classify("redis_command", "{\"command\":\"KEYS *\"}", empty).Rejected, "an empty deniedCommands does not re-allow KEYS");

            ConnectorInvocationContext allowed = RedisTestHelper.Context(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "h" }, { "deniedCommands", "FLUSHALL" }, { "allowKeysCommand", "true" } }, null));
            EffectClassification keys = RedisTestHelper.Classify("redis_command", "{\"command\":\"KEYS user:*\"}", allowed);
            RedisTestHelper.Assert(!keys.Rejected && keys.Effect == EffectEnum.Read, "allowKeysCommand makes KEYS Read");
            RedisTestHelper.Assert(RedisTestHelper.Classify("redis_command", "{\"command\":\"FLUSHALL\"}", allowed).Rejected, "other denials still apply");

            ConnectorInvocationContext both = RedisTestHelper.Context(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "h" }, { "deniedCommands", "KEYS" }, { "allowKeysCommand", "true" } }, null));
            RedisTestHelper.Assert(RedisTestHelper.Classify("redis_command", "{\"command\":\"KEYS *\"}", both).Rejected, "an explicit denial beats allowKeysCommand");
            return Task.CompletedTask;
        }

        private static Task DeletesAlwaysApproval(CancellationToken token)
        {
            ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), null));
            EffectClassification tool = RedisTestHelper.Classify("redis_delete", "{\"keys\":[\"a\"]}", ctx);
            RedisTestHelper.Assert(tool.Effect == EffectEnum.Destructive && tool.AlwaysRequireApproval, "redis_delete always requires approval");
            foreach (string command in new[] { "DEL a", "del a b", "UNLINK a", "GETDEL a", "FLUSHDB" })
            {
                EffectClassification c = RedisTestHelper.Classify("redis_command", "{\"command\":\"" + command + "\"}", ctx);
                RedisTestHelper.Assert(c.Effect == EffectEnum.Destructive && c.AlwaysRequireApproval, command + " always requires approval");
            }
            EffectClassification hdel = RedisTestHelper.Classify("redis_command", "{\"command\":\"HDEL h f\"}", ctx);
            RedisTestHelper.Assert(hdel.Effect == EffectEnum.Destructive && !hdel.AlwaysRequireApproval, "field deletes follow the access mode");
            return Task.CompletedTask;
        }

        private static Task OptionsDefaults(CancellationToken token)
        {
            RedisConnectionOptions o = RedisConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "endpoints", "h" } }));
            RedisTestHelper.Assert(o.Endpoints.Count == 1 && o.Endpoints[0].Port == 6379, "default port");
            RedisTestHelper.Assert(o.Database == 0 && o.MaxKeysScanned == 10000 && o.MaxValueBytes == 65536 && o.ConnectTimeoutSeconds == 5 && o.CommandTimeoutSeconds == 30, "defaults");
            RedisTestHelper.Assert(o.ClusterMode == RedisClusterModeEnum.Auto && !o.UseSsl && o.VerifyTls && o.DeniedCommands.Count == 0 && !o.AllowKeysCommand, "mode, tls, denied");
            RedisConnectionOptions c = RedisConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "endpoints", "h" }, { "maxKeysScanned", "5" }, { "maxValueBytes", "999999999" }, { "clusterMode", "SENTINEL" }, { "deniedCommands", "config set\nflushall" } }));
            RedisTestHelper.Assert(c.MaxKeysScanned == 100 && c.MaxValueBytes == 16 * 1024 * 1024, "clamped");
            RedisTestHelper.Assert(c.ClusterMode == RedisClusterModeEnum.Sentinel && c.Endpoints[0].Port == 26379, "sentinel default port");
            RedisTestHelper.Assert(c.DeniedCommands.Contains("CONFIG SET") && c.DeniedCommands.Contains("FLUSHALL"), "denied list parsed");
            return Task.CompletedTask;
        }

        private static Task OptionsConfiguration(CancellationToken token)
        {
            RedisConnectionOptions o = RedisConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string>
            {
                { "endpoints", "a:7000\nb:7001" }, { "database", "4" }, { "useSsl", "true" }, { "sslHost", "cache.example.test" }, { "verifyTls", "false" }, { "connectTimeoutSeconds", "3" }, { "commandTimeoutSeconds", "12" }, { "clientName", "agent" }
            }));
            ConfigurationOptions c = o.BuildConfiguration("pw-1", "secret-user");
            RedisTestHelper.Assert(c.EndPoints.Count == 2, "two endpoints");
            RedisTestHelper.Assert(c.Password == "pw-1" && c.User == "secret-user", "password and user from the secret");
            RedisTestHelper.Assert(c.Ssl && c.SslHost == "cache.example.test", "tls and sni");
            RedisTestHelper.Assert(c.DefaultDatabase == 4 && c.ConnectTimeout == 3000 && c.AsyncTimeout == 12000 && c.ClientName == "agent", "db, timeouts, client name");
            RedisTestHelper.Assert(c.AllowAdmin && c.AbortOnConnectFail, "admin and abort on connect fail");
            RedisTestHelper.Assert(!c.ToString(false).Contains("pw-1", StringComparison.Ordinal), "password not in the printable configuration");

            RedisConnectionOptions u = RedisConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "endpoints", "a" }, { "username", "setting-user" } }));
            RedisTestHelper.Assert(u.BuildConfiguration(null, "secret-user").User == "setting-user", "setting username wins");

            RedisConnectionOptions s = RedisConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "endpoints", "s1" }, { "clusterMode", "Sentinel" }, { "sentinelServiceName", "mymaster" } }));
            ConfigurationOptions sc = s.BuildConfiguration("pw", null);
            RedisTestHelper.Assert(sc.ServiceName == "mymaster" && sc.SentinelPassword == "pw", "sentinel service and password");

            bool threw = false;
            try
            {
                RedisConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "endpoints", "s1" }, { "clusterMode", "Sentinel" } })).BuildConfiguration(null, null);
            }
            catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.InvalidSetting)
            {
                threw = true;
            }
            RedisTestHelper.Assert(threw, "sentinel without service name is invalid_setting");

            threw = false;
            try
            {
                RedisConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string>())).BuildConfiguration(null, null);
            }
            catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.InvalidSetting)
            {
                threw = true;
            }
            RedisTestHelper.Assert(threw, "no endpoints is invalid_setting");
            return Task.CompletedTask;
        }

        private static Task Tokenizer(CancellationToken token)
        {
            RedisTestHelper.Assert(RedisCommandTokenizer.Tokenize("GET  user:1 ").SequenceEqual(new[] { "GET", "user:1" }), "plain");
            RedisTestHelper.Assert(RedisCommandTokenizer.Tokenize("SET k \"hello world\"").SequenceEqual(new[] { "SET", "k", "hello world" }), "double quotes");
            RedisTestHelper.Assert(RedisCommandTokenizer.Tokenize("SET k 'it\\'s'").SequenceEqual(new[] { "SET", "k", "it's" }), "single quotes");
            RedisTestHelper.Assert(RedisCommandTokenizer.Tokenize("SET k \"a\\nb\\x41\"")[2] == "a\nbA", "escapes");
            RedisTestHelper.Assert(RedisCommandTokenizer.Tokenize("SET k \"\"")[2] == String.Empty && RedisCommandTokenizer.Tokenize("SET k \"\"").Count == 3, "empty quoted argument");
            RedisTestHelper.Assert(RedisCommandTokenizer.Tokenize("   ").Count == 0, "blank");
            bool threw = false;
            try
            {
                RedisCommandTokenizer.Tokenize("SET k \"unterminated");
            }
            catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.InvalidArgument)
            {
                threw = true;
            }
            RedisTestHelper.Assert(threw, "unterminated quote");
            return Task.CompletedTask;
        }

        private static Task CommandTable(CancellationToken token)
        {
            List<string> names = RedisCommandTable.Names();
            RedisTestHelper.Assert(names.Count >= 240, "at least 240 top-level commands, got " + names.Count);
            string[] mustHave = new string[]
            {
                "GET", "SET", "DEL", "UNLINK", "SCAN", "HGETALL", "LPUSH", "BLPOP", "SADD", "ZADD", "XADD", "XREADGROUP", "EVAL", "FCALL", "FUNCTION", "SCRIPT",
                "CONFIG", "CLIENT", "CLUSTER", "SENTINEL", "ACL", "MEMORY", "LATENCY", "SLOWLOG", "OBJECT", "XINFO", "XGROUP", "PUBSUB", "COMMAND", "MODULE",
                "FLUSHALL", "FLUSHDB", "SHUTDOWN", "WAITAOF", "LCS", "LMPOP", "ZMPOP", "SINTERCARD", "EXPIRETIME", "SORT_RO", "BITFIELD_RO", "SPUBLISH", "GETEX", "GETDEL"
            };
            foreach (string n in mustHave) RedisTestHelper.Assert(RedisCommandTable.Find(n) != null, "table has " + n);
            int entries = 0;
            foreach (string entry in RedisCommandTable.AllEntries())
            {
                List<string> tokens = entry.Split(' ').ToList();
                EffectClassification c = RedisCommandClassifier.Classify(tokens, new RedisClassifierOptions { Denied = new HashSet<string>() });
                RedisTestHelper.Assert(!String.IsNullOrEmpty(c.Reason), "reason for " + entry);
                entries++;
            }
            RedisTestHelper.Assert(entries >= 400, "at least 400 command and subcommand entries, got " + entries);
            RedisTestHelper.Assert(RedisCommandClassifier.Classify(new List<string> { "BRANDNEW" }).Effect == EffectEnum.Destructive, "unknown is Destructive");
            return Task.CompletedTask;
        }

        private static Task ClassifyTools(CancellationToken token)
        {
            ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), null));
            EffectClassification set = RedisTestHelper.Classify("redis_set", "{\"key\":\"user:1\",\"value\":\"abc\",\"ttlSeconds\":60}", ctx);
            RedisTestHelper.Assert(set.Effect == EffectEnum.Write && set.ApprovalSummary.Contains("\"user:1\"", StringComparison.Ordinal) && set.ApprovalSummary.Contains("3 bytes", StringComparison.Ordinal) && set.ApprovalSummary.Contains("60s", StringComparison.Ordinal) && set.ApprovalSummary.EndsWith("on cache", StringComparison.Ordinal), "set summary: " + set.ApprovalSummary);
            EffectClassification exp = RedisTestHelper.Classify("redis_expire", "{\"key\":\"k\",\"persist\":true}", ctx);
            RedisTestHelper.Assert(exp.Effect == EffectEnum.Write && exp.ApprovalSummary.Contains("Remove the TTL", StringComparison.Ordinal), "expire summary");
            EffectClassification del = RedisTestHelper.Classify("redis_delete", "{\"keys\":[\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\"]}", ctx);
            RedisTestHelper.Assert(del.Effect == EffectEnum.Destructive && del.ApprovalSummary.Contains("7 Redis key(s)", StringComparison.Ordinal) && del.ApprovalSummary.Contains("2 more", StringComparison.Ordinal), "delete summary: " + del.ApprovalSummary);
            foreach (string name in new[] { "redis_info", "redis_scan", "redis_get", "redis_ttl", "redis_memory_usage", "redis_slowlog", "redis_client_list", "redis_latency", "redis_bigkeys" })
            {
                EffectClassification c = RedisTestHelper.Classify(name, "{}", ctx);
                RedisTestHelper.Assert(c.Effect == EffectEnum.Read && !c.Rejected && !String.IsNullOrEmpty(c.Reason), name + " is Read");
            }
            return Task.CompletedTask;
        }

        private static Task ClassifyCommand(CancellationToken token)
        {
            ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(new Dictionary<string, string> { { "endpoints", "h" }, { "deniedCommands", "KEYS\nCONFIG SET" }, { "readOnlyCommands", "JSON.GET" } }, null));
            EffectClassification get = RedisTestHelper.Classify("redis_command", "{\"command\":\"HGETALL user:42\"}", ctx);
            RedisTestHelper.Assert(get.Effect == EffectEnum.Read && get.ApprovalSummary == "Run Redis \"HGETALL user:42\" on cache", "read summary: " + get.ApprovalSummary);
            EffectClassification flush = RedisTestHelper.Classify("redis_command", "{\"command\":\"FLUSHALL\"}", ctx);
            RedisTestHelper.Assert(flush.Effect == EffectEnum.Destructive && flush.AlwaysRequireApproval, "flushall always needs approval");
            EffectClassification cfg = RedisTestHelper.Classify("redis_command", "{\"command\":\"config set maxmemory 1gb\"}", ctx);
            RedisTestHelper.Assert(cfg.Rejected, "CONFIG SET denied by the resource");
            EffectClassification json = RedisTestHelper.Classify("redis_command", "{\"command\":\"JSON.GET doc\"}", ctx);
            RedisTestHelper.Assert(json.Effect == EffectEnum.Read, "readOnlyCommands");
            EffectClassification args = RedisTestHelper.Classify("redis_command", "{\"command\":\"SET\",\"args\":[\"k\",\"a value with spaces that is rather long indeed, longer than forty\"]}", ctx);
            RedisTestHelper.Assert(args.Effect == EffectEnum.Write && args.ApprovalSummary.Contains("...", StringComparison.Ordinal) && args.ApprovalSummary.Contains("'", StringComparison.Ordinal), "args appended and shortened: " + args.ApprovalSummary);
            EffectClassification many = RedisTestHelper.Classify("redis_command", "{\"command\":\"MSET a 1 b 2 c 3 d 4\"}", ctx);
            RedisTestHelper.Assert(many.ApprovalSummary.Contains("(+4 more args)", StringComparison.Ordinal), "argument count: " + many.ApprovalSummary);
            EffectClassification bad = RedisTestHelper.Classify("redis_command", "{\"command\":\"SET k \\\"open\"}", ctx);
            RedisTestHelper.Assert(bad.Rejected, "unparseable command is rejected");
            EffectClassification empty = RedisTestHelper.Classify("redis_command", "{\"command\":\"\"}", ctx);
            RedisTestHelper.Assert(empty.Rejected, "empty command is rejected");
            EffectClassification noContext = RedisTestHelper.Tool("redis_command").Classify(null!, new ToolArguments("{\"command\":\"KEYS *\"}"));
            RedisTestHelper.Assert(noContext.Rejected, "KEYS rejected with default options");
            return Task.CompletedTask;
        }

        private static Task DisplayValues(CancellationToken token)
        {
            RedisDisplayValue text = RedisDisplayValue.From((RedisValue)"hello", 100);
            RedisTestHelper.Assert(text.Text == "hello" && !text.Binary && !text.Truncated && text.Bytes == 5 && text.ToInline() == "\"hello\"", "text");
            RedisDisplayValue bin = RedisDisplayValue.From((RedisValue)new byte[] { 0, 1, 255 }, 100);
            RedisTestHelper.Assert(bin.Binary && bin.Text == "AAH/" && bin.ToInline() == "base64:AAH/", "binary");
            RedisDisplayValue cut = RedisDisplayValue.From((RedisValue)"ééééé", 5);
            RedisTestHelper.Assert(cut.Truncated && cut.Text == "éé" && cut.Bytes == 10, "cut on a character boundary: " + cut.Text);
            RedisDisplayValue nil = RedisDisplayValue.From(RedisValue.Null, 10);
            RedisTestHelper.Assert(nil.Text == null && nil.ToInline() == "(nil)", "nil");
            RedisTestHelper.Assert(RedisDisplayValue.From((RedisValue)"a\nb\"", 100).ToInline() == "\"a\\nb\\\"\"", "escaped inline");
            return Task.CompletedTask;
        }

        private static Task ResultRenderer(CancellationToken token)
        {
            RedisResult nested = RedisResult.Create(new RedisResult[]
            {
                RedisResult.Create((RedisValue)"a"),
                RedisResult.Create((RedisValue)5, ResultType.Integer),
                RedisResult.Create(new RedisResult[] { RedisResult.Create((RedisValue)"x"), RedisResult.Create(RedisValue.Null) })
            });
            RedisResultRenderer r = new RedisResultRenderer();
            string text = r.RenderText(nested);
            RedisTestHelper.Assert(text.Contains("1) \"a\"", StringComparison.Ordinal) && text.Contains("2) (integer) 5", StringComparison.Ordinal) && text.Contains("1) \"x\"", StringComparison.Ordinal) && text.Contains("(nil)", StringComparison.Ordinal), "text: " + text);
            string json = r.RenderJson(nested);
            RedisTestHelper.Assert(json == "[\"a\",5,[\"x\",null]]", "json: " + json);

            RedisResult big = RedisResult.Create(Enumerable.Range(0, 50).Select(i => (RedisValue)i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
            RedisResultRenderer capped = new RedisResultRenderer { MaxElements = 10 };
            string cappedText = capped.RenderText(big);
            RedisTestHelper.Assert(capped.Truncated && capped.Elements == 10 && cappedText.Contains("40 more element(s) omitted", StringComparison.Ordinal), "element cap: " + cappedText);
            return Task.CompletedTask;
        }

        private static Task ScanCursor(CancellationToken token)
        {
            RedisScanner.ParseCursor(RedisScanner.FormatCursor(2, 1792), 3, out int node, out ulong pos);
            RedisTestHelper.Assert(node == 2 && pos == 1792, "round trip");
            foreach (string bad in new[] { "x", "1", "1:2:3", "-1:0", "a:b" })
            {
                bool threw = false;
                try
                {
                    RedisScanner.ParseCursor(bad, 3, out int _, out ulong _);
                }
                catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.InvalidArgument)
                {
                    threw = true;
                }
                RedisTestHelper.Assert(threw, "bad cursor " + bad);
            }
            bool outOfRange = false;
            try
            {
                RedisScanner.ParseCursor("5:0", 3, out int _, out ulong _);
            }
            catch (ConnectorException)
            {
                outOfRange = true;
            }
            RedisTestHelper.Assert(outOfRange, "node index beyond the primaries");
            RedisKey binary = RedisScanner.ParseKey("base64:AAH/");
            RedisTestHelper.Assert(((byte[])binary!).SequenceEqual(new byte[] { 0, 1, 255 }) && RedisScanner.DisplayKey(binary) == "base64:AAH/", "binary key round trip");
            RedisTestHelper.Assert(RedisScanner.DisplayKey(RedisScanner.ParseKey("user:1")) == "user:1", "text key");
            return Task.CompletedTask;
        }

        private static Task ErrorMapping(CancellationToken token)
        {
            Dictionary<string, string> map = new Dictionary<string, string>
            {
                { "WRONGTYPE Operation against a key holding the wrong kind of value", ConnectorErrorCodes.InvalidArgument },
                { "ERR unknown command 'FOO'", ConnectorErrorCodes.InvalidArgument },
                { "ERR wrong number of arguments for 'get' command", ConnectorErrorCodes.InvalidArgument },
                { "ERR syntax error", ConnectorErrorCodes.InvalidArgument },
                { "ERR value is not an integer or out of range", ConnectorErrorCodes.InvalidArgument },
                { "NOPERM User reader has no permissions to run the 'set' command", ConnectorErrorCodes.PermissionDenied },
                { "READONLY You can't write against a read only replica.", ConnectorErrorCodes.PermissionDenied },
                { "WRONGPASS invalid username-password pair or user is disabled.", ConnectorErrorCodes.AuthFailed },
                { "NOAUTH Authentication required.", ConnectorErrorCodes.AuthFailed },
                { "BUSY Redis is busy running a script.", ConnectorErrorCodes.UpstreamError },
                { "LOADING Redis is loading the dataset in memory", ConnectorErrorCodes.UpstreamError },
                { "CROSSSLOT Keys in request don't hash to the same slot", ConnectorErrorCodes.InvalidArgument },
                { "OOM command not allowed when used memory > 'maxmemory'.", ConnectorErrorCodes.UpstreamError }
            };
            foreach (KeyValuePair<string, string> kv in map)
            {
                ConnectorException e = RedisErrorMapper.MapServerError(kv.Key, "test", null);
                RedisTestHelper.Assert(e.Code == kv.Value, kv.Key + " -> " + e.Code);
                RedisTestHelper.Assert(e.Message.Contains(kv.Key.Split(' ')[0], StringComparison.Ordinal), "message keeps the reply");
            }
            RedisTestHelper.Assert(RedisErrorMapper.Map(new RedisServerException(RedisErrorKind.Unknown, CommandFlags.None, "NOPERM x"), "op").Code == ConnectorErrorCodes.PermissionDenied, "server exception");
            RedisTestHelper.Assert(RedisErrorMapper.Map(new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, CommandFlags.None, "auth", null!, CommandStatus.Unknown), "op").Code == ConnectorErrorCodes.AuthFailed, "auth failure type");
            RedisTestHelper.Assert(RedisErrorMapper.Map(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "down", null!, CommandStatus.Unknown), "op").Code == ConnectorErrorCodes.ConnectionFailed, "unable to connect");
            RedisTestHelper.Assert(RedisErrorMapper.Map(new RedisTimeoutException(CommandFlags.None, "slow", CommandStatus.Sent), "op").Code == ConnectorErrorCodes.Timeout, "timeout");
            return Task.CompletedTask;
        }

        private static async Task ArgumentsBeforeConnect(CancellationToken token)
        {
            ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), null));
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_get", "{}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "get without key");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_get", "{\"key\":\"base64:@@@\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "bad base64 key");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_scan", "{\"cursor\":\"nope\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "bad cursor");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_scan", "{\"type\":\"json\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "bad type");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_info", "{\"sections\":[\"memory; flushall\"]}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "bad section");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_latency", "{\"event\":\"x y\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "bad event");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_ttl", "{\"keys\":[]}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "no keys");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_ttl", RedisTestHelper.Json(new { keys = Enumerable.Range(0, 101).Select(i => "k" + i).ToArray() }), ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "too many keys");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_set", "{\"key\":\"k\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "set without value");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_set", "{\"key\":\"k\",\"value\":\"v\",\"ttlSeconds\":5,\"keepTtl\":true}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "ttl and keepTtl");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_set", "{\"key\":\"k\",\"value\":\"v\",\"condition\":\"sometimes\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "bad condition");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_set", RedisTestHelper.Json(new { key = "k", value = new string('x', 70000) }), ctx, token).ConfigureAwait(false), ConnectorErrorCodes.TooLarge, "value over maxValueBytes");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_expire", "{\"key\":\"k\",\"ttlSeconds\":5,\"persist\":true}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "ttl and persist");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_expire", "{\"key\":\"k\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.InvalidArgument, "neither ttl nor persist");
            ToolResult keys = await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"KEYS *\"}", ctx, token).ConfigureAwait(false);
            RedisTestHelper.AssertError(keys, ConnectorErrorCodes.PermissionDenied, "KEYS rejected");
            RedisTestHelper.Assert(keys.Text.Contains("redis_scan", StringComparison.Ordinal), "KEYS rejection points to redis_scan");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"BLPOP q 0\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.PermissionDenied, "blocking rejected");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"SELECT 2\"}", ctx, token).ConfigureAwait(false), ConnectorErrorCodes.PermissionDenied, "SELECT rejected");
        }

        private static async Task ReadOnlyDefense(CancellationToken token)
        {
            ConnectorInvocationContext ro = RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), null), AccessModeEnum.ReadOnly);
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_set", "{\"key\":\"k\",\"value\":\"v\"}", ro, token).ConfigureAwait(false), ConnectorErrorCodes.PermissionDenied, "set on read-only");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_expire", "{\"key\":\"k\",\"ttlSeconds\":5}", ro, token).ConfigureAwait(false), ConnectorErrorCodes.PermissionDenied, "expire on read-only");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_delete", "{\"keys\":[\"k\"]}", ro, token).ConfigureAwait(false), ConnectorErrorCodes.PermissionDenied, "delete on read-only");
            RedisTestHelper.AssertError(await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"DEL k\"}", ro, token).ConfigureAwait(false), ConnectorErrorCodes.PermissionDenied, "DEL on read-only");
            ToolResult read = await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"GET k\"}", ro, token).ConfigureAwait(false);
            RedisTestHelper.AssertError(read, ConnectorErrorCodes.ConnectionFailed, "a read on read-only gets as far as connecting");
        }

        private static async Task ConnectionRefused(CancellationToken token)
        {
            Stopwatch sw = Stopwatch.StartNew();
            ToolResult r = await RedisTestHelper.RunAsync("redis_info", "{}", RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), null)), token).ConfigureAwait(false);
            RedisTestHelper.AssertError(r, ConnectorErrorCodes.ConnectionFailed, "refused");
            RedisTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(20), "fails within the connect timeout");
            RedisTestHelper.Assert(r.Metrics.ContainsKey("latencyMs"), "latency metric");
            ConnectorHealthResult h = await RedisTestHelper.Connector.TestConnectionAsync(RedisTestHelper.Resource(Closed(), null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(!h.Success && h.ErrorCode == ConnectorErrorCodes.ConnectionFailed, "health: " + h.ErrorCode + " " + h.Message);
            ConnectorHealthResult invalid = await RedisTestHelper.Connector.TestConnectionAsync(RedisTestHelper.Resource(new Dictionary<string, string>(), null), token).ConfigureAwait(false);
            RedisTestHelper.Assert(!invalid.Success && invalid.ErrorCode == ConnectorErrorCodes.InvalidSetting, "health without endpoints: " + invalid.ErrorCode);
        }

        private static async Task Timeout(CancellationToken token)
        {
            using (SilentTcpServer silent = new SilentTcpServer())
            {
                Dictionary<string, string> s = new Dictionary<string, string> { { "endpoints", "127.0.0.1:" + silent.Port }, { "connectTimeoutSeconds", "30" } };
                Stopwatch sw = Stopwatch.StartNew();
                ToolResult r = await RedisTestHelper.RunAsync("redis_info", "{}", RedisTestHelper.Context(RedisTestHelper.Resource(s, "pw"), AccessModeEnum.ReadWrite, 1), token).ConfigureAwait(false);
                RedisTestHelper.AssertError(r, ConnectorErrorCodes.Timeout, "silent server");
                RedisTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(10), "returns near the 1 second timeout, took " + sw.Elapsed);
                RedisTestHelper.Assert(silent.Accepted >= 1, "the client connected");
            }
        }

        private static async Task Cancellation(CancellationToken token)
        {
            using (CancellationTokenSource pre = new CancellationTokenSource())
            {
                pre.Cancel();
                bool threw = false;
                try
                {
                    await RedisTestHelper.RunAsync("redis_info", "{}", RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), null)), pre.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
                RedisTestHelper.Assert(threw, "cancelled before start");
            }

            using (SilentTcpServer silent = new SilentTcpServer())
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400)))
            {
                Dictionary<string, string> s = new Dictionary<string, string> { { "endpoints", "127.0.0.1:" + silent.Port }, { "connectTimeoutSeconds", "30" } };
                Stopwatch sw = Stopwatch.StartNew();
                bool threw = false;
                try
                {
                    await RedisTestHelper.RunAsync("redis_scan", "{}", RedisTestHelper.Context(RedisTestHelper.Resource(s, null)), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
                RedisTestHelper.Assert(threw && sw.Elapsed < TimeSpan.FromSeconds(10), "cancelled while connecting, after " + sw.Elapsed);
            }
        }

        private static async Task SecretNotEchoed(CancellationToken token)
        {
            string password = "s3cr3t-redis-pw-" + Guid.NewGuid().ToString("N");
            using (SilentTcpServer silent = new SilentTcpServer())
            {
                Dictionary<string, string> s = new Dictionary<string, string> { { "endpoints", "127.0.0.1:" + silent.Port } };
                ToolResult timeout = await RedisTestHelper.RunAsync("redis_get", "{\"key\":\"k\"}", RedisTestHelper.Context(RedisTestHelper.Resource(s, password), AccessModeEnum.ReadWrite, 1), token).ConfigureAwait(false);
                RedisTestHelper.Assert(!timeout.Text.Contains(password, StringComparison.Ordinal), "timeout text");
            }
            ToolResult refused = await RedisTestHelper.RunAsync("redis_get", "{\"key\":\"k\"}", RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), password)), token).ConfigureAwait(false);
            RedisTestHelper.Assert(!refused.Text.Contains(password, StringComparison.Ordinal) && !(refused.StructuredJson ?? String.Empty).Contains(password, StringComparison.Ordinal), "refused text");
            ConnectorHealthResult h = await RedisTestHelper.Connector.TestConnectionAsync(RedisTestHelper.Resource(Closed(), password), token).ConfigureAwait(false);
            RedisTestHelper.Assert(!h.Message.Contains(password, StringComparison.Ordinal) && !h.Details.Values.Any(v => v.Contains(password, StringComparison.Ordinal)), "health message");
            EffectClassification c = RedisTestHelper.Classify("redis_command", "{\"command\":\"GET k\"}", RedisTestHelper.Context(RedisTestHelper.Resource(Closed(), password)));
            RedisTestHelper.Assert(!c.ApprovalSummary.Contains(password, StringComparison.Ordinal), "summary");
        }

        #endregion
    }
}
