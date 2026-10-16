namespace Test.Shared.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Inference;
    using Hugo.Core.Security;
    using Hugo.Core.Settings;
    using Hugo.Core.Telemetry;
    using SyslogLogging;
    using Test.Shared.Connectors.Http;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>Pre-sink logging redaction, external-library ingress, bounded export and ownership tests.</summary>
    public static class LoggingSuite
    {
        /// <summary>Build provider-independent logging tests.</summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            string id = "logging-pipeline";
            return new List<TestSuiteDescriptor> { new TestSuiteDescriptor(id, "Logging pipeline", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(id, "sinks", "Every severity and library/structured/async entry is scrubbed before console, file, syslog and log activity sinks", SinksAsync),
                new TestCaseDescriptor(id, "known-values", "Known-secret registration excludes sessions and has bounded fail-closed count/character storage", KnownAsync),
                new TestCaseDescriptor(id, "ownership", "Adapters dispose private ingress, preserve caller sinks and safely adapt a reused output module", OwnershipAsync),
                new TestCaseDescriptor(id, "radiant-loki", "Radiant sends scrubbed correlated OTLP log records to Loki with the configured tenant header", ExportAsync),
                new TestCaseDescriptor(id, "outage", "Invalid exporters and remote outages do not break logging; bounded queues drop instead of blocking", OutageAsync)
            }) };
        }

        private static async Task SinksAsync(CancellationToken token)
        {
            string secret = "LogPlant_" + Guid.NewGuid().ToString("N");
            SecretScrubber.Shared.AddKnownValue(secret);
            string path = Path.Combine(Path.GetTempPath(), "hugo-log-" + Guid.NewGuid().ToString("N") + ".log");
            TextWriter original = Console.Out;
            using (StringWriter console = new StringWriter())
            using (UdpClient receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (LoggingModule output = new LoggingModule())
            {
                output.Settings.DetectConsole = false;
                output.Settings.EnableConsole = false;
                output.Settings.FileLogging = FileLoggingMode.Disabled;
                output.Settings.MinimumSeverity = Severity.Debug;
                output.Settings.EnableTracing = true;
                List<string> activities = new List<string>();
                List<string> delivered = new List<string>();
                output.MessageLogged += entry => delivered.Add(entry.ToJson());
                using (ActivityListener listener = new ActivityListener())
                {
                    listener.ShouldListenTo = source => source.Name.Contains("Syslog", StringComparison.OrdinalIgnoreCase);
                    listener.Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded;
                    listener.ActivityStopped = activity => activities.Add(String.Join(" ", activity.TagObjects.Select(tag => tag.Value?.ToString())));
                    ActivitySource.AddActivityListener(listener);
                    using (HugoLog log = new HugoLog(output))
                    {
                        Assert.Equal(0, log.LibraryLogger.Servers.Count, "Library ingress must have no default syslog server");
                        Assert.True(!log.LibraryLogger.Settings.EnableConsole && log.LibraryLogger.Settings.FileLogging == FileLoggingMode.Disabled && !log.LibraryLogger.Settings.EnableTracing, "Library ingress has an unredacted sink.");
                        // Retaining and mutating the caller's module configures the sanitized output, never library ingress.
                        output.Settings.EnableConsole = true;
                        output.Settings.FileLogging = FileLoggingMode.SingleLogFile;
                        output.Settings.LogFilename = path;
                        output.Servers = new List<SyslogServer> { new SyslogServer("127.0.0.1", ((IPEndPoint)receiver.Client.LocalEndPoint!).Port) };
                        Console.SetOut(console);
                        try
                        {
                            foreach (Severity severity in Enum.GetValues<Severity>()) log.Log(severity, "severity " + severity + " " + secret + " password=unknownPassword123");
                            LogEntry structured = new LogEntry(Severity.Info, "structured " + secret, new InvalidOperationException("outer " + secret, new Exception("inner " + secret)));
                            structured.Source = secret;
                            structured.CorrelationId = secret;
                            structured.WithProperty("nested", new Dictionary<string, string> { { "credential", secret } });
                            structured.WithProperty(secret, secret);
                            string fresh = "fresh unknown password with \"quoted\" spaces";
                            structured.WithProperty("password", fresh);
                            structured.WithProperty("apiKey", "fresh unknown token with spaces");
                            structured.WithProperty("nestedUnknown", new Dictionary<string, string> { { "password", fresh } });
                            log.Info("quoted password=\"fresh password with spaces\"; apiKey='fresh token with spaces'");
                            log.LogEntry(structured);
                            await log.LogEntryAsync(structured, token).ConfigureAwait(false);
                            log.LibraryLogger.Info("external library info " + secret);
                            log.LibraryLogger.Exception(new Exception("external exception " + secret), "library", "send");
                            await log.LibraryLogger.LogEntryAsync(structured, token).ConfigureAwait(false);
                            string local = console.ToString() + await File.ReadAllTextAsync(path, token).ConfigureAwait(false) + String.Join("\n", delivered);
                            Assert.True(local.Contains("[redacted:", StringComparison.Ordinal), "Sinks lack redacted records.");
                            Assert.True(!local.Contains(secret, StringComparison.Ordinal) && !local.Contains("unknownPassword123", StringComparison.Ordinal), "Secret reached console/file sink.");
                            Assert.True(!local.Contains("fresh", StringComparison.Ordinal), "Unknown structured or quoted secret reached a sink.");
                            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                                string syslog = Encoding.UTF8.GetString((await receiver.ReceiveAsync(timeout.Token).ConfigureAwait(false)).Buffer);
                                Assert.True(!syslog.Contains(secret, StringComparison.Ordinal) && !syslog.Contains("unknownPassword123", StringComparison.Ordinal), "Secret reached syslog.");
                            }
                            Assert.True(activities.All(text => !text.Contains(secret, StringComparison.Ordinal)), "Raw ingress reached log activities.");
                            Assert.True(structured.Message.Contains(secret, StringComparison.Ordinal), "Caller entry was modified.");
                        }
                        finally { Console.SetOut(original); }
                    }
                }
            }
            if (File.Exists(path)) File.Delete(path);
        }

        private static Task KnownAsync(CancellationToken token)
        {
            string secret = "CipherPlant_" + Guid.NewGuid().ToString("N");
            SecretCipher cipher = new SecretCipher(RandomNumberGenerator.GetBytes(32), 1);
            string encrypted = cipher.Encrypt(Constants.KeyPurposeSecrets, secret, "row");
            Assert.True(!SecretScrubber.Shared.Scrub("standalone " + secret)!.Contains(secret, StringComparison.Ordinal), "Encrypt boundary did not register secret.");
            Assert.Equal(secret, cipher.Decrypt(Constants.KeyPurposeSecrets, encrypted, "row"), "Cipher unchanged");
            int count = SecretScrubber.Shared.KnownValueCount;
            cipher.Encrypt(Constants.KeyPurposeSessions, "session payload " + Guid.NewGuid().ToString("N"), "issuer");
            Assert.Equal(count, SecretScrubber.Shared.KnownValueCount, "Session payload polluted known secrets");
            SecretScrubber bounded = new SecretScrubber { MaximumKnownValues = 100, MaximumKnownCharacters = 1024 };
            bounded.AddKnownValue(new string('x', 2048));
            Assert.True(bounded.KnownValueCount == 0 && bounded.Scrub("ordinary log") == "[redacted:capacity]", "Character budget must fail closed without retaining oversized values.");
            bounded.ClearKnownValues();
            bounded.MaximumKnownCharacters = 100000;
            for (int i = 0; i < 200; i++) bounded.AddKnownValue("unique-secret-" + i);
            Assert.True(bounded.KnownValueCount <= 100 && bounded.Scrub("ordinary log") == "[redacted:capacity]", "Count budget is unbounded or evicts secrets.");
            bounded.ClearKnownValues();
            Assert.Equal("ordinary log", bounded.Scrub("ordinary log"), "Explicit reset restores scrubber");
            LoggingJsonRecord json = new LoggingJsonRecord
            {
                Password = "fresh password with \"escaped quotes\" and spaces",
                ApiKey = "fresh token with 'single quotes' and spaces",
                Nested = new Dictionary<string, string> { { "password", "fresh nested with \"quotes\" and spaces" }, { "accessToken", "fresh nested token with spaces" } }
            };
            SecretScrubber overlap = new SecretScrubber();
            overlap.AddKnownValues(new[] { "password", "known", "redacted" });
            string localJson = overlap.Scrub(JsonSerializer.Serialize(json))!;
            LoggingJsonRecord localParsed = JsonSerializer.Deserialize<LoggingJsonRecord>(localJson)!;
            Assert.Equal("[redacted:password_pair]", localParsed.Password, "Known literal key name cannot hide an unknown password value");
            Assert.Equal("[redacted:known] [redacted:known]", overlap.Scrub("known redacted"), "Known values cannot recursively alter inserted markers");
            Assert.Equal("a [redacted:known] in raw text", overlap.Scrub("a password in raw text"), "Literal secrets outside assignments must still be redacted");
            Assert.True(!localJson.Contains("fresh", StringComparison.Ordinal), "Overlapping known values bypassed nested pattern redaction.");
            foreach (string value in new[]
            {
                "fresh prefix Bearer ABCDEFGHIJKLMNOP fresh suffix",
                "fresh prefix https://user:uriPassword@127.0.0.1 fresh suffix",
                "fresh prefix AKIAABCDEFGHIJKLMNOP fresh suffix",
                "fresh prefix -----BEGIN PRIVATE KEY-----\nfresh key material\n-----END PRIVATE KEY----- fresh suffix",
                "fresh prefix password='fresh inner' fresh suffix",
                "fresh prefix apiKey='fresh inner' fresh suffix"
            })
            {
                LoggingJsonRecord mixed = new LoggingJsonRecord { Password = value, ApiKey = value, Nested = new Dictionary<string, string> { { "clientSecret", value } } };
                string sanitized = overlap.Scrub(JsonSerializer.Serialize(mixed))!;
                LoggingJsonRecord framed = JsonSerializer.Deserialize<LoggingJsonRecord>(sanitized)!;
                Assert.True(!sanitized.Contains("fresh", StringComparison.Ordinal), "Nested pattern split an outer quoted assignment: " + sanitized);
                Assert.True(framed.Nested["clientSecret"].Contains("[redacted:", StringComparison.Ordinal), "Camel-case nested clientSecret escaped redaction.");
            }
            foreach (string standalone in new[]
            {
                "apiKey=\"fresh prefix password='fresh inner' fresh suffix\"",
                "password='fresh prefix apiKey=\"fresh inner\" fresh suffix'",
                "password=fresh prefix Bearer ABCDEFGHIJKLMNOP fresh suffix",
                "clientSecret=fresh prefix https://user:uriPassword@127.0.0.1 fresh suffix",
                "password=fresh prefix -----BEGIN PRIVATE KEY-----\nfresh key material\n-----END PRIVATE KEY----- fresh suffix"
            })
            {
                Assert.True(!overlap.Scrub(standalone)!.Contains("fresh", StringComparison.Ordinal), "Inner redaction split a standalone assignment.");
            }
            string redactedJson = SecretScrubber.Shared.Scrub(JsonSerializer.Serialize(json))!;
            LoggingJsonRecord parsed = JsonSerializer.Deserialize<LoggingJsonRecord>(redactedJson)!;
            Assert.True(!redactedJson.Contains("fresh", StringComparison.Ordinal), "Quoted sensitive values were only partly redacted.");
            Assert.True(parsed.Password.Contains("[redacted:", StringComparison.Ordinal) && parsed.ApiKey.Contains("[redacted:", StringComparison.Ordinal), "Top-level JSON fields did not survive redaction.");
            Assert.True(parsed.Nested["password"].Contains("[redacted:", StringComparison.Ordinal) && parsed.Nested["accessToken"].Contains("[redacted:", StringComparison.Ordinal), "Nested JSON fields did not survive redaction.");
            return Task.CompletedTask;
        }

        private static Task OwnershipAsync(CancellationToken token)
        {
            using (LoggingModule output = new LoggingModule())
            {
                output.Settings.EnableConsole = false;
                HugoLog first = output;
                LoggingModule ingress = first.LibraryLogger;
                first.Dispose();
                bool disposed = false;
                try { ingress.Info("after disposal"); } catch (ObjectDisposedException) { disposed = true; }
                Assert.True(disposed, "Private ingress not disposed.");
                output.Info("caller-owned output remains live");
                using (HugoLog second = output)
                {
                    Assert.True(!ReferenceEquals(first, second), "Disposed adapter returned from cache.");
                    second.Info("reused output");
                }
            }
            ModelClientFactory factory = new ModelClientFactory();
            LoggingModule ownedIngress = factory.Logging.LibraryLogger;
            factory.Dispose();
            bool closed = false;
            try { ownedIngress.Info("after disposal"); } catch (ObjectDisposedException) { closed = true; }
            Assert.True(closed, "Default model factory leaked its logging adapter.");
            return Task.CompletedTask;
        }

        private static async Task ExportAsync(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            using (LoggingModule output = new LoggingModule())
            {
                stub.Handler = request => HttpStubResponse.Text(200, String.Empty, "application/x-protobuf");
                output.Settings.EnableConsole = false;
                string secret = "LokiPlant_" + Guid.NewGuid().ToString("N");
                SecretScrubber.Shared.AddKnownValue(secret);
                TelemetrySettings settings = new TelemetrySettings { Enabled = true, PrometheusEnable = false, OtlpEnable = false, LokiEnable = true, LokiEndpoint = stub.BaseAddress + "/otlp", LokiTenantId = "test-tenant", Exemplars = false };
                using (HugoTelemetry telemetry = new HugoTelemetry(settings, "test-node", null))
                using (HugoLog log = new HugoLog(output))
                {
                    Assert.True(telemetry.Active, "Radiant did not start.");
                    log.Export = telemetry.Log;
                    string trace;
                    string span;
                    using (Activity activity = new Activity("log-correlation").Start())
                    {
                        trace = activity.TraceId.ToHexString();
                        span = activity.SpanId.ToHexString();
                        log.Info("export-marker " + secret);
                        log.LibraryLogger.Warn("library-export-marker " + secret);
                    }
                    using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(15));
                        while (!stub.Requests.Any(request => request.Body.Contains("library-export-marker", StringComparison.Ordinal))) await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                    }
                    HttpStubRequest request = stub.Requests.First(r => r.Path == "/otlp/v1/logs" && r.Body.Contains("export-marker", StringComparison.Ordinal));
                    Assert.Equal("test-tenant", request.Header("X-Scope-OrgID"), "Loki tenant header");
                    Assert.True(request.Body.Contains("[redacted:", StringComparison.Ordinal) && !request.Body.Contains(secret, StringComparison.Ordinal), "OTLP contained plaintext secret.");
                    Assert.True(request.Body.Contains(trace, StringComparison.Ordinal) && request.Body.Contains(span, StringComparison.Ordinal), "Queued logs lost captured trace/span attributes.");
                    Assert.True(request.Body.Contains("app", StringComparison.Ordinal) && request.Body.Contains("hugo", StringComparison.Ordinal), "Missing collector labels.");
                }
            }
        }

        private static Task OutageAsync(CancellationToken token)
        {
            List<string> diagnostics = new List<string>();
            using (HugoTelemetry invalid = new HugoTelemetry(new TelemetrySettings { PrometheusEnable = false, LokiEnable = true, LokiEndpoint = "not-a-uri" }, "test", message => diagnostics.Add(message)))
            {
                Assert.True(!invalid.Active && diagnostics.Any(), "Invalid telemetry should disable itself best-effort.");
            }
            using (HugoTelemetry invalid = new HugoTelemetry(new TelemetrySettings { PrometheusEnable = false, LokiEnable = true, LokiEndpoint = "not-a-uri" }, "throwing-diagnostic", message => throw new InvalidOperationException("diagnostic callback failure")))
            {
                Assert.True(!invalid.Active, "Throwing diagnostic callback defeated best-effort initialization.");
            }
            TelemetrySettings settings = new TelemetrySettings { PrometheusEnable = false, LokiEnable = true, LokiEndpoint = "http://127.0.0.1:1/otlp", ExportTimeoutMs = 1000, LogDrainTimeoutMs = 1000, LogQueueCapacity = 1, Exemplars = false };
            Stopwatch elapsed = Stopwatch.StartNew();
            using (HugoTelemetry telemetry = new HugoTelemetry(settings, "outage", null))
            {
                Parallel.For(0, 10000, i => telemetry.Log(new LogEntry(Severity.Info, "queue-pressure-marker " + i)));
                Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "Logging blocked on remote exporter outage.");
                Assert.True(telemetry.LogsDropped > 0, "Queue pressure did not exercise the bounded drop path.");
            }
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), "Exporter outage blocked graceful disposal.");
            return Task.CompletedTask;
        }
    }
}
