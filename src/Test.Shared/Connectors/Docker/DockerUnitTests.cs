namespace Test.Shared.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Docker;
    using Test.Shared.Connectors.Ssh;
    using Touchstone.Core;

    /// <summary>
    /// Docker connector unit cases (no daemon): descriptor, schemas, settings and validation, endpoint and TLS parsing,
    /// per-call classification, environment redaction, formatting, stats math, event collection, and error mapping.
    /// </summary>
    public static class DockerUnitTests
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "connectors.docker";

        #endregion

        #region Private-Members

        private static readonly DockerConnector _Connector = new DockerConnector();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the unit cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "descriptor", "Descriptor has type, family, category, icon, slot, and instructions", Descriptor);
            Add(cases, "settings-schema", "Settings schema lists every documented key with defaults and ranges", SettingsSchema);
            Add(cases, "tools", "Tool set, names, base effects, and per-call flags", ToolsShape);
            Add(cases, "tool-schemas-parse", "Every tool input schema parses and every property is described", ToolSchemas);
            Add(cases, "options-defaults", "Options apply defaults and clamp ranges", OptionsDefaults);
            Add(cases, "endpoints", "Endpoints parse for unix, tcp, and npipe; ssh and junk are refused", Endpoints);
            Add(cases, "validate", "Validation checks the endpoint, TLS material, and ranges", Validate);
            Add(cases, "tls-credentials", "TLS client credentials load from PEM and honor tlsVerify", TlsCredentials);
            Add(cases, "classify-read-tools", "Read tools classify as Read with summaries", ClassifyReadTools);
            Add(cases, "classify-container-action", "Container actions classify per action and fail closed", ClassifyContainerAction);
            Add(cases, "classify-exec", "docker_exec is Destructive and names the command", ClassifyExec);
            Add(cases, "environment-redaction", "Environment values are redacted and names kept", EnvironmentRedaction);
            Add(cases, "format-helpers", "Short IDs, sizes, ports, names, and container references", FormatHelpers);
            Add(cases, "parse-time", "Time arguments accept seconds, durations, and ISO 8601", ParseTime);
            Add(cases, "stats-math", "CPU, memory, network, and block I/O follow docker stats", StatsMath);
            Add(cases, "event-collector", "The event collector caps events and cancels when full", EventCollector);
            Add(cases, "error-mapping", "Docker and network exceptions map to standard error codes", ErrorMapping);
            Add(cases, "tail-buffer", "Tail buffer keeps the end and counts the total", TailBuffer);
            Add(cases, "execute-invalid-reference", "A malformed container reference is refused before connecting", ExecuteInvalidReference);
            Add(cases, "execute-readonly-offline", "A ReadOnly binding refuses changes without contacting the daemon", ExecuteReadOnlyOffline);
            Add(cases, "events-window-refused", "docker_events refuses a window over maxEventsWindowSeconds instead of clipping it", EventsWindowRefused);
            Add(cases, "endpoint-help-schemes", "The endpoint help names every accepted scheme", EndpointHelpSchemes);
            Add(cases, "connection-failures", "Refused and unsupported endpoints map to errors", ConnectionFailures);
            Add(cases, "execute-cancelled", "A cancelled token throws OperationCanceledException", ExecuteCancelled);
            return cases;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
        }

        private static IConnectorTool Tool(string name)
        {
            IConnectorTool? tool = _Connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        private static EffectClassification Classify(string tool, object args)
        {
            using (DockerTestResource r = new DockerTestResource("unit", new Dictionary<string, string>(), null))
            {
                return Tool(tool).Classify(r.Invocation(), new ToolArguments(JsonSerializer.Serialize(args)));
            }
        }

        private static async Task<ConnectorValidationResult> ValidateWith(Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets, CancellationToken token)
        {
            using (DockerTestResource r = new DockerTestResource("validate", settings, secrets))
            {
                return await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
            }
        }

        private static Dictionary<string, Dictionary<string, string>> CertificateSecrets(bool withCa = true)
        {
            using (RSA rsa = RSA.Create(2048))
            {
                CertificateRequest req = new CertificateRequest("CN=hugo-docker-client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using (X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
                {
                    Dictionary<string, string> fields = new Dictionary<string, string>
                    {
                        { "certificate", cert.ExportCertificatePem() },
                        { "privateKey", rsa.ExportPkcs8PrivateKeyPem() }
                    };
                    if (withCa) fields["caCertificate"] = cert.ExportCertificatePem();
                    return new Dictionary<string, Dictionary<string, string>> { { "tlsClientCertificate", fields } };
                }
            }
        }

        private static Task Descriptor(CancellationToken token)
        {
            ConnectorDescriptor d = _Connector.Descriptor;
            SshAssert.True(d.Type == "docker" && d.Family == "docker", "type and family");
            SshAssert.True(d.Category == ConnectorCategoryEnum.Orchestration, "category");
            SshAssert.True(d.IconKey == "container", "icon");
            SshAssert.True(d.DefaultInstructionsKey == "connector.docker.instructions", "instructions key");
            SshAssert.Contains(d.DefaultInstructions, "docker_ps", "instructions mention docker_ps");
            SshAssert.NotContains(d.DefaultInstructions, "\u2014", "instructions (em-dash)");
            SshAssert.True(d.SecretSlots.Count == 1 && d.SecretSlots[0].Name == "tlsClientCertificate" && d.SecretSlots[0].AcceptedKinds.Contains(SecretKindEnum.Certificate), "TLS slot");
            SshAssert.True(!d.SecretSlots[0].Required, "slot optional");
            return Task.CompletedTask;
        }

        private static Task SettingsSchema(CancellationToken token)
        {
            ConnectorSettingsSchema s = _Connector.Descriptor.SettingsSchema;
            foreach (string key in new[] { "endpoint", "tlsVerify", "requestTimeoutSeconds", "maxLogLines", "showEnvironmentValues", "maxEventsWindowSeconds", "execTimeoutSeconds", "maxStatsContainers" })
                SshAssert.True(s.Find(key) != null, "setting " + key);
            SshAssert.True(s.Find("endpoint")!.DefaultValue == "unix:///var/run/docker.sock" && s.Find("endpoint")!.Required, "endpoint default");
            SshAssert.True(s.Find("tlsVerify")!.DefaultValue == "false", "tlsVerify default");
            SshAssert.True(s.Find("showEnvironmentValues")!.DefaultValue == "false", "env redacted by default");
            SshAssert.True(s.Find("maxLogLines")!.DefaultValue == "500", "maxLogLines");
            SshAssert.True(s.Settings.All(x => !String.IsNullOrEmpty(x.Help)), "every setting has help");
            return Task.CompletedTask;
        }

        private static Task ToolsShape(CancellationToken token)
        {
            Dictionary<string, EffectEnum> expected = new Dictionary<string, EffectEnum>
            {
                { "docker_ps", EffectEnum.Read },
                { "docker_inspect", EffectEnum.Read },
                { "docker_logs", EffectEnum.Read },
                { "docker_stats", EffectEnum.Read },
                { "docker_images", EffectEnum.Read },
                { "docker_events", EffectEnum.Read },
                { "docker_container_action", EffectEnum.Write },
                { "docker_exec", EffectEnum.Destructive }
            };
            SshAssert.True(_Connector.Tools.Count == expected.Count, "tool count");
            foreach (KeyValuePair<string, EffectEnum> kv in expected)
            {
                ToolDescriptor d = Tool(kv.Key).Descriptor;
                SshAssert.True(d.BaseEffect == kv.Value, kv.Key + " base effect");
                SshAssert.True(d.Description.Length > 80, kv.Key + " description");
                SshAssert.True(!String.IsNullOrEmpty(d.Title), kv.Key + " title");
            }
            SshAssert.True(Tool("docker_container_action").Descriptor.ClassifiedPerCall, "action per call");
            return Task.CompletedTask;
        }

        private static Task ToolSchemas(CancellationToken token)
        {
            foreach (IConnectorTool tool in _Connector.Tools)
            {
                SshSchemaShape? shape = JsonSerializer.Deserialize<SshSchemaShape>(tool.Descriptor.InputSchemaJson);
                SshAssert.True(shape != null && shape.Type == "object", tool.Descriptor.Name + " schema type");
                SshAssert.True(shape!.Properties != null, tool.Descriptor.Name + " properties");
                SshAssert.True(shape.AdditionalProperties == false, tool.Descriptor.Name + " additionalProperties false");
                SshAssert.True(!shape.Properties!.ContainsKey("resource"), tool.Descriptor.Name + " does not declare resource");
                foreach (KeyValuePair<string, SshSchemaProperty> p in shape.Properties!)
                {
                    SshAssert.True(!String.IsNullOrWhiteSpace(p.Value.Description) && p.Value.Description!.Length > 20, tool.Descriptor.Name + "." + p.Key + " description");
                    SshAssert.True(!String.IsNullOrEmpty(p.Value.Type), tool.Descriptor.Name + "." + p.Key + " type");
                }
                foreach (string required in shape.Required ?? new List<string>())
                    SshAssert.True(shape.Properties!.ContainsKey(required), tool.Descriptor.Name + " required " + required);
                SshAssert.NotContains(tool.Descriptor.InputSchemaJson + tool.Descriptor.Description, "\u2014", tool.Descriptor.Name + " em-dash");
            }
            return Task.CompletedTask;
        }

        private static Task OptionsDefaults(CancellationToken token)
        {
            DockerOptions d = DockerOptions.FromSettings(new ResourceSettings(null));
            SshAssert.True(d.Endpoint == "unix:///var/run/docker.sock" && !d.TlsVerify && !d.ShowEnvironmentValues, "defaults");
            SshAssert.True(d.RequestTimeoutSeconds == 30 && d.MaxLogLines == 500 && d.MaxEventsWindowSeconds == 3600 && d.ExecTimeoutSeconds == 60 && d.MaxStatsContainers == 20, "numeric defaults");
            DockerOptions o = DockerOptions.FromSettings(new ResourceSettings(new Dictionary<string, string>
            {
                { "endpoint", "  tcp://10.0.0.5:2376  " },
                { "maxLogLines", "0" },
                { "maxEventsWindowSeconds", "5" },
                { "execTimeoutSeconds", "99999" },
                { "maxStatsContainers", "1000" }
            }));
            SshAssert.True(o.Endpoint == "tcp://10.0.0.5:2376", "endpoint trimmed");
            SshAssert.True(o.MaxLogLines == 1 && o.MaxEventsWindowSeconds == 60 && o.ExecTimeoutSeconds == 900 && o.MaxStatsContainers == 100, "clamped");
            return Task.CompletedTask;
        }

        private static Task Endpoints(CancellationToken token)
        {
            SshAssert.True(DockerSessionFactory.ParseEndpoint("unix:///var/run/docker.sock").Scheme == "unix", "unix");
            SshAssert.True(DockerSessionFactory.ParseEndpoint("tcp://10.0.0.5:2376").Port == 2376, "tcp");
            SshAssert.True(DockerSessionFactory.ParseEndpoint("npipe://./pipe/docker_engine").Scheme == "npipe", "npipe");
            Dictionary<string, string> bad = new Dictionary<string, string>
            {
                { "ssh://user@host", "not_supported" },
                { "unix://", "invalid_setting" },
                { "ftp://host", "invalid_setting" },
                { "not a uri", "invalid_setting" },
                { "", "invalid_setting" }
            };
            foreach (KeyValuePair<string, string> kv in bad)
            {
                try
                {
                    DockerSessionFactory.ParseEndpoint(kv.Key);
                    throw new InvalidOperationException("Expected '" + kv.Key + "' to be refused.");
                }
                catch (ConnectorException e)
                {
                    SshAssert.True(e.Code == kv.Value, kv.Key + ": " + e.Code);
                }
            }
            return Task.CompletedTask;
        }

        private static async Task Validate(CancellationToken token)
        {
            ConnectorValidationResult ok = await ValidateWith(new Dictionary<string, string> { { "endpoint", "tcp://127.0.0.1:2375" } }, null, token).ConfigureAwait(false);
            SshAssert.True(ok.IsValid, "plain tcp valid");
            ConnectorValidationResult defaults = await ValidateWith(new Dictionary<string, string>(), null, token).ConfigureAwait(false);
            SshAssert.True(defaults.IsValid, "default socket valid");
            ConnectorValidationResult ssh = await ValidateWith(new Dictionary<string, string> { { "endpoint", "ssh://admin@host" } }, null, token).ConfigureAwait(false);
            SshAssert.True(ssh.Errors.Any(e => e.Field == "endpoint" && e.Code == "not_supported"), "ssh not supported");
            ConnectorValidationResult tlsNoCert = await ValidateWith(new Dictionary<string, string> { { "endpoint", "tcp://h:2376" }, { "tlsVerify", "true" } }, null, token).ConfigureAwait(false);
            SshAssert.True(tlsNoCert.Errors.Any(e => e.Field == "tlsClientCertificate" && e.Code == "required"), "tlsVerify needs a certificate");
            ConnectorValidationResult tlsOk = await ValidateWith(new Dictionary<string, string> { { "endpoint", "tcp://h:2376" }, { "tlsVerify", "true" } }, CertificateSecrets(), token).ConfigureAwait(false);
            SshAssert.True(tlsOk.IsValid, "TLS valid: " + String.Join("; ", tlsOk.Errors.Select(e => e.Message)));
            ConnectorValidationResult certOnSocket = await ValidateWith(new Dictionary<string, string>(), CertificateSecrets(), token).ConfigureAwait(false);
            SshAssert.True(certOnSocket.Errors.Any(e => e.Field == "endpoint" && e.Code == "invalid_value"), "certificate with a unix socket");
            Dictionary<string, Dictionary<string, string>> badPem = new Dictionary<string, Dictionary<string, string>> { { "tlsClientCertificate", new Dictionary<string, string> { { "certificate", "junk" }, { "privateKey", "junk" } } } };
            ConnectorValidationResult junk = await ValidateWith(new Dictionary<string, string> { { "endpoint", "tcp://h:2376" } }, badPem, token).ConfigureAwait(false);
            SshAssert.True(junk.Errors.Any(e => e.Field == "tlsClientCertificate" && e.Code == "invalid_format"), "bad PEM");
            SshAssert.True(junk.Errors.All(e => !e.Message.Contains("junk", StringComparison.Ordinal)), "no secret material in messages");
            Dictionary<string, Dictionary<string, string>> noKey = CertificateSecrets();
            noKey["tlsClientCertificate"].Remove("privateKey");
            ConnectorValidationResult missingKey = await ValidateWith(new Dictionary<string, string> { { "endpoint", "tcp://h:2376" } }, noKey, token).ConfigureAwait(false);
            SshAssert.True(missingKey.Errors.Any(e => e.Field == "tlsClientCertificate"), "private key required");
            ConnectorValidationResult ranges = await ValidateWith(new Dictionary<string, string> { { "maxLogLines", "0" }, { "execTimeoutSeconds", "1000" } }, null, token).ConfigureAwait(false);
            SshAssert.True(ranges.Errors.Count(e => e.Code == "out_of_range") == 2, "ranges");
        }

        private static Task TlsCredentials(CancellationToken token)
        {
            using (DockerTestResource r = new DockerTestResource("tls", new Dictionary<string, string>(), CertificateSecrets()))
            {
                global::Docker.DotNet.X509.CertificateCredentials? verified = DockerSessionFactory.LoadCredentials(r.Context, true);
                SshAssert.True(verified != null && verified.TlsEnabled, "credentials with TLS");
                SshAssert.True(verified!.ServerCertificateValidationCallback != null, "CA validation callback");
                global::Docker.DotNet.X509.CertificateCredentials? unverified = DockerSessionFactory.LoadCredentials(r.Context, false);
                SshAssert.True(unverified!.ServerCertificateValidationCallback!(new object(), null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors), "unverified accepts any server certificate");
            }
            using (DockerTestResource r = new DockerTestResource("notls", new Dictionary<string, string>(), null))
            {
                SshAssert.True(DockerSessionFactory.LoadCredentials(r.Context, false) == null, "no credentials without a certificate");
            }
            return Task.CompletedTask;
        }

        private static Task ClassifyReadTools(CancellationToken token)
        {
            List<KeyValuePair<string, object>> reads = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("docker_ps", new { all = true }),
                new KeyValuePair<string, object>("docker_inspect", new { target = "web" }),
                new KeyValuePair<string, object>("docker_logs", new { container = "web" }),
                new KeyValuePair<string, object>("docker_stats", new { }),
                new KeyValuePair<string, object>("docker_images", new { }),
                new KeyValuePair<string, object>("docker_events", new { since = "10m" })
            };
            foreach (KeyValuePair<string, object> call in reads)
            {
                EffectClassification c = Classify(call.Key, call.Value);
                SshAssert.True(c.Effect == EffectEnum.Read && !c.Rejected, call.Key + " Read");
                SshAssert.Contains(c.ApprovalSummary, "unit", call.Key + " summary names the target");
            }
            return Task.CompletedTask;
        }

        private static Task ClassifyContainerAction(CancellationToken token)
        {
            Dictionary<string, EffectEnum> table = new Dictionary<string, EffectEnum>
            {
                { "start", EffectEnum.Write },
                { "restart", EffectEnum.Write },
                { "stop", EffectEnum.Destructive },
                { "kill", EffectEnum.Destructive },
                { "remove", EffectEnum.Destructive },
                { "STOP", EffectEnum.Destructive },
                { "Restart", EffectEnum.Write },
                { "pause", EffectEnum.Destructive },
                { "", EffectEnum.Destructive },
                { "rm -rf", EffectEnum.Destructive }
            };
            foreach (KeyValuePair<string, EffectEnum> kv in table)
            {
                EffectClassification c = Classify("docker_container_action", new { container = "web", action = kv.Key });
                SshAssert.True(c.Effect == kv.Value, "action '" + kv.Key + "' is " + kv.Value + " but was " + c.Effect);
                SshAssert.True(!String.IsNullOrEmpty(c.Reason), "reason for " + kv.Key);
            }
            SshAssert.Contains(Classify("docker_container_action", new { container = "web", action = "restart" }).ApprovalSummary, "Restart container web on unit", "summary");
            SshAssert.Contains(Classify("docker_container_action", new { container = "web", action = "remove", force = true }).Reason, "even while running", "force reason");
            return Task.CompletedTask;
        }

        private static Task ClassifyExec(CancellationToken token)
        {
            EffectClassification c = Classify("docker_exec", new { container = "web", command = new[] { "ls", "-la", "/" }, user = "root" });
            SshAssert.True(c.Effect == EffectEnum.Destructive, "exec D");
            SshAssert.Contains(c.ApprovalSummary, "Run \"ls -la /\" in container web as root on unit", "summary: " + c.ApprovalSummary);
            return Task.CompletedTask;
        }

        private static Task EnvironmentRedaction(CancellationToken token)
        {
            IList<string> redacted = DockerEnvironmentRedactor.Redact(new List<string> { "PATH=/usr/bin", "DB_PASSWORD=hunter2", "EMPTY=", "NOEQUALS", "URL=postgres://u:p@h/db?x=1" });
            SshAssert.True(redacted[0] == "PATH=<redacted>", "value redacted");
            SshAssert.True(redacted[1] == "DB_PASSWORD=<redacted>", "secret redacted");
            SshAssert.True(redacted[2] == "EMPTY=", "empty stays empty");
            SshAssert.True(redacted[3] == "NOEQUALS", "no value unchanged");
            SshAssert.True(redacted[4] == "URL=<redacted>", "everything after the first = redacted");
            SshAssert.True(!String.Join(",", redacted).Contains("hunter2", StringComparison.Ordinal), "no secret");
            return Task.CompletedTask;
        }

        private static Task FormatHelpers(CancellationToken token)
        {
            SshAssert.True(DockerFormat.ShortId("sha256:0123456789abcdef0123") == "0123456789ab", "short id with prefix");
            SshAssert.True(DockerFormat.ShortId("abc") == "abc", "short id short");
            SshAssert.True(DockerFormat.Bytes(999) == "999B" && DockerFormat.Bytes(1500) == "1.5kB" && DockerFormat.Bytes(2_000_000_000) == "2GB", "sizes: " + DockerFormat.Bytes(1500));
            SshAssert.True(DockerFormat.Ports(new List<PortSummary> { new PortSummary { IP = "127.0.0.1", PrivatePort = 80, PublicPort = 8080, Type = "tcp" }, new PortSummary { PrivatePort = 443, Type = "tcp" } }) == "127.0.0.1:8080->80/tcp, 443/tcp", "ports");
            SshAssert.True(DockerFormat.Name(new List<string> { "/web" }) == "web" && DockerFormat.Name(null) == String.Empty, "names");
            SshAssert.True(DockerFormat.IsContainerReference("web-1") && DockerFormat.IsContainerReference("/web_1.a") && DockerFormat.IsContainerReference("0123abcd"), "valid references");
            SshAssert.True(!DockerFormat.IsContainerReference("") && !DockerFormat.IsContainerReference("web;rm") && !DockerFormat.IsContainerReference("../x") && !DockerFormat.IsContainerReference("a b"), "invalid references");
            DateTime now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            SshAssert.True(DockerFormat.Age(now.AddSeconds(-30), now) == "30s" && DockerFormat.Age(now.AddHours(-3), now) == "3h" && DockerFormat.Age(now.AddDays(-5), now) == "5d", "ages");
            SshAssert.True(DockerFormat.UnixSeconds(new DateTime(1970, 1, 1, 0, 1, 0, DateTimeKind.Utc)) == "60", "unix seconds");
            return Task.CompletedTask;
        }

        private static Task ParseTime(CancellationToken token)
        {
            DateTime now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            SshAssert.True(DockerFormat.ParseTime(null, now, "since") == null && DockerFormat.ParseTime(" ", now, "since") == null, "empty");
            SshAssert.True(DockerFormat.ParseTime("300", now, "since") == now.AddSeconds(-300), "seconds");
            SshAssert.True(DockerFormat.ParseTime("10m", now, "since") == now.AddMinutes(-10), "minutes");
            SshAssert.True(DockerFormat.ParseTime("2h", now, "since") == now.AddHours(-2), "hours");
            SshAssert.True(DockerFormat.ParseTime("1d", now, "since") == now.AddDays(-1), "days");
            SshAssert.True(DockerFormat.ParseTime("2026-05-31T10:00:00Z", now, "since") == new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc), "ISO");
            foreach (string bad in new[] { "yesterday", "10w", "-5m", "1.5h" })
            {
                try
                {
                    DockerFormat.ParseTime(bad, now, "since");
                    throw new InvalidOperationException("Expected '" + bad + "' to be refused.");
                }
                catch (ConnectorException e)
                {
                    SshAssert.True(e.Code == "invalid_argument", bad);
                }
            }
            return Task.CompletedTask;
        }

        private static Task StatsMath(CancellationToken token)
        {
            ContainerStatsResponse s = new ContainerStatsResponse
            {
                CPUStats = new CPUStats { CPUUsage = new CPUUsage { TotalUsage = 3_000_000 }, SystemUsage = 20_000_000, OnlineCPUs = 4 },
                PreCPUStats = new CPUStats { CPUUsage = new CPUUsage { TotalUsage = 1_000_000 }, SystemUsage = 10_000_000, OnlineCPUs = 4 },
                MemoryStats = new MemoryStats { Usage = 100_000_000, Limit = 400_000_000, Stats = new Dictionary<string, ulong> { { "inactive_file", 20_000_000 } } },
                Networks = new Dictionary<string, NetworkStats> { { "eth0", new NetworkStats { RxBytes = 1000, TxBytes = 2000 } }, { "eth1", new NetworkStats { RxBytes = 5, TxBytes = 5 } } },
                BlkioStats = new BlkioStats { IoServiceBytesRecursive = new List<BlkioStatEntry> { new BlkioStatEntry { Op = "read", Value = 100 }, new BlkioStatEntry { Op = "Write", Value = 50 }, new BlkioStatEntry { Op = "read", Value = 1 } } }
            };
            SshAssert.True(Math.Abs(DockerStatsMath.CpuPercent(s) - 80.0) < 0.001, "cpu percent: " + DockerStatsMath.CpuPercent(s));
            SshAssert.True(DockerStatsMath.MemoryUsed(s) == 80_000_000, "memory without cache");
            SshAssert.True(Math.Abs(DockerStatsMath.MemoryPercent(s) - 20.0) < 0.001, "memory percent");
            SshAssert.True(DockerStatsMath.NetworkReceived(s) == 1005 && DockerStatsMath.NetworkSent(s) == 2005, "network");
            SshAssert.True(DockerStatsMath.BlockBytes(s, "read") == 101 && DockerStatsMath.BlockBytes(s, "write") == 50, "block I/O");
            ContainerStatsResponse first = new ContainerStatsResponse { CPUStats = s.CPUStats, MemoryStats = new MemoryStats { Usage = 10 } };
            SshAssert.True(DockerStatsMath.CpuPercent(first) == 0, "no previous sample");
            SshAssert.True(DockerStatsMath.MemoryPercent(first) == 0 && DockerStatsMath.MemoryUsed(first) == 10, "no limit");
            ContainerStatsResponse v1 = new ContainerStatsResponse { MemoryStats = new MemoryStats { Usage = 100, Stats = new Dictionary<string, ulong> { { "cache", 30 } } } };
            SshAssert.True(DockerStatsMath.MemoryUsed(v1) == 70, "cgroup v1 cache");
            return Task.CompletedTask;
        }

        private static Task EventCollector(CancellationToken token)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                DockerEventCollector c = new DockerEventCollector(2, cts);
                c.Report(new Message { Action = "start" });
                c.Report(new Message { Action = "die" });
                SshAssert.True(!c.Overflowed && !cts.IsCancellationRequested, "within capacity");
                c.Report(new Message { Action = "destroy" });
                SshAssert.True(c.Overflowed && cts.IsCancellationRequested, "cancelled when full");
                SshAssert.True(c.Events().Select(e => e.Action).SequenceEqual(new[] { "start", "die" }), "keeps the first events");
            }
            return Task.CompletedTask;
        }

        private static Task ErrorMapping(CancellationToken token)
        {
            SshAssert.True(DockerErrorMapper.Map(new DockerContainerNotFoundException(HttpStatusCode.NotFound, "{\"message\":\"No such container: x\"}"), "container x", "e").Code == "not_found", "container not found");
            ConnectorException conflict = DockerErrorMapper.Map(new DockerApiException(HttpStatusCode.Conflict, "{\"message\":\"You cannot remove a running container\"}"), "container x", "e");
            SshAssert.True(conflict.Code == "invalid_argument" && conflict.Message.Contains("You cannot remove a running container", StringComparison.Ordinal), "409 with daemon message");
            SshAssert.True(DockerErrorMapper.Map(new DockerApiException(HttpStatusCode.Forbidden, "denied"), "x", "e").Code == "permission_denied", "403");
            SshAssert.True(DockerErrorMapper.Map(new DockerApiException(HttpStatusCode.Unauthorized, ""), "x", "e").Code == "auth_failed", "401");
            SshAssert.True(DockerErrorMapper.Map(new DockerApiException(HttpStatusCode.InternalServerError, "boom"), "x", "e").Code == "upstream_error", "500");
            SshAssert.True(DockerErrorMapper.Map(new DockerApiException(HttpStatusCode.BadRequest, "bad"), "x", "e").Code == "invalid_argument", "400");
            SshAssert.True(DockerErrorMapper.Map(new HttpRequestException("Connection refused"), "x", "tcp://h:1").Code == "connection_failed", "network");
            SshAssert.True(DockerErrorMapper.Map(new TaskCanceledException(), "x", "e").Code == "timeout", "timeout");
            SshAssert.True(DockerErrorMapper.Map(new System.Security.Authentication.AuthenticationException("bad cert"), "x", "e").Code == "connection_failed", "tls");
            SshAssert.True(DockerErrorMapper.Map(new InvalidOperationException("x"), "x", "e").Code == "upstream_error", "other");
            SshAssert.True(DockerErrorMapper.Map(new AggregateException(new HttpRequestException("refused")), "x", "e").Code == "connection_failed", "aggregate unwrapped");
            SshAssert.True(DockerErrorMapper.IsConnectionBroken(new HttpRequestException("x")) && !DockerErrorMapper.IsConnectionBroken(new DockerApiException(HttpStatusCode.NotFound, "")), "broken connection detection");
            return Task.CompletedTask;
        }

        private static Task TailBuffer(CancellationToken token)
        {
            DockerTailBuffer b = new DockerTailBuffer(8);
            byte[] data = Encoding.UTF8.GetBytes("0123456789");
            b.Write(data, 0, 10);
            SshAssert.True(b.Text() == "23456789" && b.Dropped && b.TotalBytes == 10, "keeps the end");
            return Task.CompletedTask;
        }

        private static async Task ExecuteInvalidReference(CancellationToken token)
        {
            using (DockerTestResource r = new DockerTestResource("badref", new Dictionary<string, string> { { "endpoint", "tcp://127.0.0.1:1" } }, null))
            {
                ToolResult logs = await Tool("docker_logs").ExecuteAsync(r.Invocation(), new ToolArguments("{\"container\":\"web; rm -rf /\"}"), token).ConfigureAwait(false);
                SshAssert.Error(logs, "invalid_argument", "logs");
                ToolResult exec = await Tool("docker_exec").ExecuteAsync(r.Invocation(), new ToolArguments("{\"container\":\"web\",\"command\":[]}"), token).ConfigureAwait(false);
                SshAssert.Error(exec, "invalid_argument", "empty command");
            }
        }

        private static async Task EventsWindowRefused(CancellationToken token)
        {
            using (DockerTestResource r = new DockerTestResource("events", new Dictionary<string, string> { { "endpoint", "tcp://127.0.0.1:1" }, { "maxEventsWindowSeconds", "3600" } }, null))
            {
                ToolResult tooLong = await Tool("docker_events").ExecuteAsync(r.Invocation(), new ToolArguments("{\"since\":\"2h\"}"), token).ConfigureAwait(false);
                SshAssert.Error(tooLong, "invalid_argument", "two hours over a one hour limit");
                SshAssert.Contains(tooLong.Text, "maxEventsWindowSeconds", "the error names the setting");
                ToolResult explicitWindow = await Tool("docker_events").ExecuteAsync(r.Invocation(), new ToolArguments("{\"since\":\"2026-10-01T00:00:00Z\",\"until\":\"2026-10-01T02:00:00Z\"}"), token).ConfigureAwait(false);
                SshAssert.Error(explicitWindow, "invalid_argument", "an explicit two hour window");
            }
        }

        private static Task EndpointHelpSchemes(CancellationToken token)
        {
            SettingDescriptor endpoint = _Connector.Descriptor.SettingsSchema.Find("endpoint")!;
            foreach (string scheme in new[] { "unix://", "tcp://", "npipe://", "http://", "https://" })
                SshAssert.Contains(endpoint.Help, scheme, "endpoint help names " + scheme);
            return Task.CompletedTask;
        }

        private static async Task ExecuteReadOnlyOffline(CancellationToken token)
        {
            using (DockerTestResource r = new DockerTestResource("ro", new Dictionary<string, string> { { "endpoint", "tcp://127.0.0.1:1" } }, null))
            {
                ConnectorInvocationContext ctx = r.Invocation(AccessModeEnum.ReadOnly);
                ToolResult start = await Tool("docker_container_action").ExecuteAsync(ctx, new ToolArguments("{\"container\":\"web\",\"action\":\"start\"}"), token).ConfigureAwait(false);
                SshAssert.Error(start, "permission_denied", "start refused");
                ToolResult exec = await Tool("docker_exec").ExecuteAsync(ctx, new ToolArguments("{\"container\":\"web\",\"command\":[\"id\"]}"), token).ConfigureAwait(false);
                SshAssert.Error(exec, "permission_denied", "exec refused");
            }
        }

        private static async Task ConnectionFailures(CancellationToken token)
        {
            using (DockerTestResource r = new DockerTestResource("refused", new Dictionary<string, string> { { "endpoint", "tcp://127.0.0.1:1" }, { "requestTimeoutSeconds", "5" } }, null))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && (h.ErrorCode == "connection_failed" || h.ErrorCode == "timeout"), "health: " + h.ErrorCode + " " + h.Message);
                ToolResult ps = await Tool("docker_ps").ExecuteAsync(r.Invocation(), new ToolArguments("{}"), token).ConfigureAwait(false);
                SshAssert.True(ps.IsError && (ps.ErrorCode == "connection_failed" || ps.ErrorCode == "timeout"), "ps: " + ps.ErrorCode + " " + ps.Text);
            }
            using (DockerTestResource r = new DockerTestResource("ssh", new Dictionary<string, string> { { "endpoint", "ssh://admin@host" } }, null))
            {
                ToolResult ps = await Tool("docker_ps").ExecuteAsync(r.Invocation(), new ToolArguments("{}"), token).ConfigureAwait(false);
                SshAssert.Error(ps, "not_supported", "ssh endpoint");
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "not_supported", "health ssh");
            }
            using (DockerTestResource r = new DockerTestResource("tlsmissing", new Dictionary<string, string> { { "endpoint", "tcp://127.0.0.1:1" }, { "tlsVerify", "true" } }, null))
            {
                ToolResult ps = await Tool("docker_ps").ExecuteAsync(r.Invocation(), new ToolArguments("{}"), token).ConfigureAwait(false);
                SshAssert.Error(ps, "secret_missing", "tlsVerify without certificate");
            }
        }

        private static async Task ExecuteCancelled(CancellationToken token)
        {
            using (DockerTestResource r = new DockerTestResource("cancel", new Dictionary<string, string> { { "endpoint", "tcp://127.0.0.1:1" } }, null))
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool thrown = false;
                try
                {
                    await Tool("docker_ps").ExecuteAsync(r.Invocation(), new ToolArguments("{}"), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    thrown = true;
                }
                SshAssert.True(thrown, "OperationCanceledException");
            }
        }

        #endregion
    }
}
