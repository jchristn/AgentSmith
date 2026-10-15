namespace Test.Shared.Connectors.Ssh
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Ssh;
    using Touchstone.Core;

    /// <summary>
    /// SSH connector integration cases against <c>docker/testbed/ssh/compose.yaml</c>. They run only when
    /// <c>HUGO_TESTBED=1</c>; otherwise every case is skipped with a reason.
    /// </summary>
    public static class SshTestbedTests
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "testbed.ssh";

        #endregion

        #region Private-Members

        private static readonly SshConnector _Connector = new SshConnector();
        private static readonly string _WrongFingerprint = "SHA256:" + new string('A', 43);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the testbed suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "test-connection-password", "Test connection with password auth reports the host key", TestConnectionPassword);
            Add(cases, "test-connection-key-ed25519", "Test connection with an ed25519 key", ct => TestConnectionKey("id_ed25519", null, ct));
            Add(cases, "test-connection-key-passphrase", "Test connection with a passphrase-protected key", ct => TestConnectionKey("id_ed25519_passphrase", SshTestbed.KeyPassphrase, ct));
            Add(cases, "test-connection-key-rsa", "Test connection with an RSA key", ct => TestConnectionKey("id_rsa", null, ct));
            Add(cases, "auth-failed-password", "A wrong password maps to auth_failed", AuthFailedPassword);
            Add(cases, "auth-failed-key-user-password", "Password login for the key-only user maps to auth_failed", AuthFailedKeyUser);
            Add(cases, "host-key-tofu", "An empty fingerprint is learned on first use and then enforced", HostKeyTofu);
            Add(cases, "host-key-mismatch", "A pinned fingerprint that does not match fails closed", HostKeyMismatch);
            Add(cases, "exec-exit-code-streams", "ssh_exec returns exit code, stdout, and stderr separately", ExecStreams);
            Add(cases, "exec-environment-workdir", "ssh_exec applies environment and working directory", ExecEnvironment);
            Add(cases, "exec-timeout", "ssh_exec stops a command at its timeout and returns partial output", ExecTimeout);
            Add(cases, "exec-cancellation", "ssh_exec honors cancellation", ExecCancellation);
            Add(cases, "exec-output-truncation", "ssh_exec bounds large output and keeps the end", ExecTruncation);
            Add(cases, "exec-session-pooling", "Calls on one resource reuse one pooled session", ExecPooling);
            Add(cases, "system-info", "ssh_system_info reports host facts", SystemInfo);
            Add(cases, "service-status", "ssh_service_status runs and reports the unit", ServiceStatus);
            Add(cases, "service-control", "ssh_service_control reports failure for an unknown unit", ServiceControl);
            Add(cases, "read-file-bytes", "ssh_read_file reads byte ranges", ReadFileBytes);
            Add(cases, "read-file-lines", "ssh_read_file reads line ranges", ReadFileLines);
            Add(cases, "read-file-errors", "ssh_read_file maps not_found and permission_denied", ReadFileErrors);
            Add(cases, "read-file-binary", "ssh_read_file returns binary content as base64", ReadFileBinary);
            Add(cases, "tail-file", "ssh_tail_file returns the last lines", TailFile);
            Add(cases, "tail-file-grep", "ssh_tail_file filters with grep", TailFileGrep);
            Add(cases, "tail-file-not-found", "ssh_tail_file maps a missing file to not_found", TailFileNotFound);
            Add(cases, "list-directory", "ssh_list_directory lists, hides dotfiles, and caps rows", ListDirectory);
            Add(cases, "list-directory-not-found", "ssh_list_directory maps a missing directory to not_found", ListDirectoryNotFound);
            Add(cases, "write-file", "ssh_write_file creates, refuses to overwrite, and overwrites on request", WriteFile);
            Add(cases, "write-file-relative", "ssh_write_file resolves relative paths against workingDirectory", WriteFileRelative);
            Add(cases, "jump-host", "Connections chain through the bastion and pin both host keys", JumpHost);
            Add(cases, "jump-host-sftp", "SFTP tools work through the bastion", JumpHostSftp);
            Add(cases, "jump-host-key-mismatch", "A wrong bastion fingerprint fails closed", JumpHostMismatch);
            return new TestSuiteDescriptor(SuiteId, "SSH connector against the testbed", cases);
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            bool skip = !SshTestbed.Enabled;
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body, null, skip, skip ? SshTestbed.SkipReason : null!));
        }

        private static IConnectorTool Tool(string name)
        {
            IConnectorTool? tool = _Connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        private static SshTestResource PasswordResource(string name, Dictionary<string, string>? extra = null, IConnectorSessionPool? pool = null)
        {
            Dictionary<string, string> s = SshTestbed.PasswordSettings();
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kv in extra) s[kv.Key] = kv.Value;
            }
            return new SshTestResource(name, s, SshTestbed.PasswordSecrets(), pool);
        }

        private static Task<ToolResult> Run(SshTestResource r, string tool, string json, CancellationToken token, ConnectorInvocationContext? ctx = null)
        {
            return Tool(tool).ExecuteAsync(ctx ?? r.Invocation(), new ToolArguments(json), token);
        }

        private static string Json(object value)
        {
            return JsonSerializer.Serialize(value);
        }

        private static async Task TestConnectionPassword(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-password"))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "success: " + h.ErrorCode + " " + h.Message);
                SshAssert.True(h.Details["hostKeyFingerprint"].StartsWith("SHA256:", StringComparison.Ordinal), "fingerprint");
                SshAssert.True(SshFingerprint.IsValid(h.Details["hostKeyFingerprint"]), "fingerprint valid");
                SshAssert.Contains(h.Details["uname"], "Linux", "uname");
                SshAssert.Contains(h.Details["serverVersion"], "OpenSSH", "server version");
                SshAssert.True(r.Learned.TryGetValue("hostKeyFingerprint", out string? learned) && learned == h.Details["hostKeyFingerprint"], "learned");
            }
        }

        private static async Task TestConnectionKey(string keyName, string? passphrase, CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("tb-key-" + keyName, SshTestbed.KeySettings(), SshTestbed.KeySecrets(keyName, passphrase)))
            {
                ConnectorValidationResult v = await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(v.IsValid, "validation: " + String.Join("; ", v.Errors.Select(e => e.Field + " " + e.Message)));
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "success: " + h.ErrorCode + " " + h.Message);
                ToolResult who = await Run(r, "ssh_exec", Json(new { command = "whoami" }), token).ConfigureAwait(false);
                SshAssert.Ok(who, "whoami");
                SshAssert.Contains(who.Text, SshTestbed.KeyUser, "whoami");
            }
        }

        private static async Task AuthFailedPassword(CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("tb-badpw", SshTestbed.PasswordSettings(), SshTestbed.PasswordSecrets("wrong-password")))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "auth_failed", "auth_failed: " + h.ErrorCode + " " + h.Message);
                SshAssert.NotContains(h.Message, "wrong-password", "no secret in message");
                ToolResult result = await Run(r, "ssh_exec", Json(new { command = "uptime" }), token).ConfigureAwait(false);
                SshAssert.Error(result, "auth_failed", "exec");
            }
        }

        private static async Task AuthFailedKeyUser(CancellationToken token)
        {
            Dictionary<string, string> s = SshTestbed.PasswordSettings();
            s["username"] = SshTestbed.KeyUser;
            using (SshTestResource r = new SshTestResource("tb-keyuser-pw", s, SshTestbed.PasswordSecrets()))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "auth_failed", "auth_failed: " + h.ErrorCode + " " + h.Message);
            }
        }

        private static async Task HostKeyTofu(CancellationToken token)
        {
            string learned;
            using (SshTestResource first = PasswordResource("tb-tofu-1"))
            {
                ToolResult result = await Run(first, "ssh_exec", Json(new { command = "true" }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "first exec");
                SshAssert.True(first.Learned.TryGetValue("hostKeyFingerprint", out string? fp) && fp != null, "learned on first use");
                learned = fp!;
            }
            using (SshTestResource pinned = PasswordResource("tb-tofu-2", new Dictionary<string, string> { { "hostKeyFingerprint", learned } }))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(pinned.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "pinned success: " + h.Message);
                SshAssert.True(h.Details["hostKeyPinned"] == "true", "pinned flag");
                SshAssert.True(pinned.Learned.IsEmpty, "nothing learned when pinned");
            }
            string bare = learned.Substring("SHA256:".Length) + "=";
            using (SshTestResource variant = PasswordResource("tb-tofu-3", new Dictionary<string, string> { { "hostKeyFingerprint", bare } }))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(variant.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "bare padded fingerprint accepted: " + h.Message);
            }
        }

        private static async Task HostKeyMismatch(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-mismatch", new Dictionary<string, string> { { "hostKeyFingerprint", _WrongFingerprint } }))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "host_key_mismatch", "health: " + h.ErrorCode + " " + h.Message);
                SshAssert.Contains(h.Message, _WrongFingerprint, "message names the expected key");
                ToolResult result = await Run(r, "ssh_exec", Json(new { command = "uptime" }), token).ConfigureAwait(false);
                SshAssert.Error(result, "host_key_mismatch", "exec");
                ToolResult list = await Run(r, "ssh_list_directory", Json(new { path = "/srv/hugo" }), token).ConfigureAwait(false);
                SshAssert.Error(list, "host_key_mismatch", "list");
                SshAssert.True(r.Learned.IsEmpty, "mismatch never learns");
            }
        }

        private static async Task ExecStreams(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-exec"))
            {
                string command = "echo hello; echo oops >&2; exit 3";
                EffectClassification c = Tool("ssh_exec").Classify(r.Invocation(), new ToolArguments(Json(new { command = command })));
                SshAssert.True(c.Effect == EffectEnum.Read, "classified Read: " + c.Reason);
                ToolResult result = await Run(r, "ssh_exec", Json(new { command = command }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "exec");
                SshAssert.Contains(result.Text, "exit_code: 3", "exit code");
                SshExecOutput? o = JsonSerializer.Deserialize<SshExecOutput>(result.StructuredJson ?? "{}");
                SshAssert.True(o != null && o.ExitCode == 3, "structured exit code");
                SshAssert.True(o!.Stdout.Trim() == "hello" && o.Stderr.Trim() == "oops", "streams: [" + o.Stdout + "] [" + o.Stderr + "]");
                SshAssert.True(result.Metrics["exitCode"] == 3, "metric");
            }
        }

        private static async Task ExecEnvironment(CancellationToken token)
        {
            Dictionary<string, string> extra = new Dictionary<string, string>
            {
                { "environment", "HUGO_TEST_VAR=xyz 123\nLANG=C" },
                { "workingDirectory", "/srv/hugo" }
            };
            using (SshTestResource r = PasswordResource("tb-env", extra))
            {
                ToolResult result = await Run(r, "ssh_exec", Json(new { command = "echo \"[$HUGO_TEST_VAR]\"; pwd" }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "exec");
                SshAssert.Contains(result.Text, "[xyz 123]", "environment");
                SshAssert.Contains(result.Text, "/srv/hugo", "working directory");
                ToolResult over = await Run(r, "ssh_exec", Json(new { command = "pwd", workingDirectory = "/tmp" }), token).ConfigureAwait(false);
                SshAssert.Contains(over.Text, "/tmp", "per-call working directory");
            }
        }

        private static async Task ExecTimeout(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-timeout"))
            {
                Stopwatch sw = Stopwatch.StartNew();
                ToolResult result = await Run(r, "ssh_exec", Json(new { command = "echo started; sleep 30; echo never", timeoutSeconds = 2 }), token).ConfigureAwait(false);
                sw.Stop();
                SshAssert.Error(result, "timeout", "exec");
                SshAssert.True(sw.Elapsed < TimeSpan.FromSeconds(12), "stopped promptly: " + sw.Elapsed);
                SshAssert.Contains(result.Text, "started", "partial output");
                SshAssert.NotContains(result.Text, "never", "command stopped");
                ToolResult after = await Run(r, "ssh_exec", Json(new { command = "echo still-usable" }), token).ConfigureAwait(false);
                SshAssert.Ok(after, "session usable after timeout");
                SshAssert.Contains(after.Text, "still-usable", "after");

                ConnectorInvocationContext shortCtx = r.Invocation(timeoutSeconds: 2);
                Stopwatch sw2 = Stopwatch.StartNew();
                ToolResult capped = await Run(r, "ssh_exec", Json(new { command = "sleep 30" }), token, shortCtx).ConfigureAwait(false);
                SshAssert.Error(capped, "timeout", "context timeout caps the command");
                SshAssert.True(sw2.Elapsed < TimeSpan.FromSeconds(12), "context timeout honored");
            }
        }

        private static async Task ExecCancellation(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-cancel"))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                ToolResult warm = await Run(r, "ssh_exec", Json(new { command = "true" }), token).ConfigureAwait(false);
                SshAssert.Ok(warm, "warm");
                cts.CancelAfter(TimeSpan.FromSeconds(1));
                Stopwatch sw = Stopwatch.StartNew();
                bool cancelled = false;
                try
                {
                    await Run(r, "ssh_exec", Json(new { command = "sleep 30" }), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                sw.Stop();
                SshAssert.True(cancelled, "OperationCanceledException");
                SshAssert.True(sw.Elapsed < TimeSpan.FromSeconds(10), "cancelled promptly: " + sw.Elapsed);
                ToolResult after = await Run(r, "ssh_exec", Json(new { command = "echo ok" }), token).ConfigureAwait(false);
                SshAssert.Ok(after, "usable after cancellation");
            }
        }

        private static async Task ExecTruncation(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-trunc"))
            {
                ConnectorInvocationContext ctx = r.Invocation(maxBytes: 4096, maxLines: 100000);
                ToolResult result = await Run(r, "ssh_exec", Json(new { command = "seq 1 200000" }), token, ctx).ConfigureAwait(false);
                SshAssert.Ok(result, "exec");
                SshAssert.True(result.Truncated, "truncated");
                SshAssert.True(result.OriginalBytes >= 1288895, "original bytes: " + result.OriginalBytes);
                SshAssert.Contains(result.Text, "200000\n", "keeps the end");
                SshAssert.Contains(result.Text, "output truncated: 1288895 bytes total", "marker reports the true total");
                SshAssert.True(System.Text.Encoding.UTF8.GetByteCount(result.Text) <= 4096 + 200, "within limit: " + System.Text.Encoding.UTF8.GetByteCount(result.Text));

                ConnectorInvocationContext lines = r.Invocation(maxLines: 10);
                ToolResult few = await Run(r, "ssh_exec", Json(new { command = "seq 1 50" }), token, lines).ConfigureAwait(false);
                SshAssert.True(few.Truncated, "line truncated");
                SshAssert.Contains(few.Text, "50\n", "line tail");
                SshAssert.NotContains(few.Text, "\n30\n", "early lines dropped");
            }
        }

        private static async Task ExecPooling(CancellationToken token)
        {
            using (CountingSessionPool pool = new CountingSessionPool())
            using (SshTestResource r = PasswordResource("tb-pool", null, pool))
            {
                for (int i = 0; i < 3; i++)
                {
                    ToolResult result = await Run(r, "ssh_exec", Json(new { command = "echo " + i.ToString(CultureInfo.InvariantCulture) }), token).ConfigureAwait(false);
                    SshAssert.Ok(result, "exec " + i);
                }
                ToolResult list = await Run(r, "ssh_list_directory", Json(new { path = "/srv/hugo" }), token).ConfigureAwait(false);
                SshAssert.Ok(list, "list");
                Task<ToolResult>[] parallel = Enumerable.Range(0, 4).Select(i => Run(r, "ssh_exec", Json(new { command = "sleep 0.2; echo p" + i.ToString(CultureInfo.InvariantCulture) }), token)).ToArray();
                ToolResult[] results = await Task.WhenAll(parallel).ConfigureAwait(false);
                SshAssert.True(results.All(x => !x.IsError), "parallel calls");
                SshAssert.True(pool.Created == 1, "one session created, was " + pool.Created);
            }
        }

        private static async Task SystemInfo(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-sysinfo"))
            {
                ToolResult result = await Run(r, "ssh_system_info", Json(new { topProcesses = 3 }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "system info");
                foreach (string section in new[] { "== hostname", "== os", "== kernel", "== uptime and load", "== cpu count", "== memory (MiB)", "== disk usage", "== top processes by cpu", "== top processes by memory" })
                    SshAssert.Contains(result.Text, section, "section");
                SshAssert.Contains(result.Text, "hugo-ssh-target", "hostname");
                SshAssert.Contains(result.Text, "Alpine", "os");
            }
        }

        private static async Task ServiceStatus(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-svc"))
            {
                ToolResult result = await Run(r, "ssh_service_status", Json(new { unit = "svc-openssh-server", journalLines = 5 }), token).ConfigureAwait(false);
                SshAssert.True(!result.IsError || result.ErrorCode == "not_supported", "status ran: " + result.ErrorCode + " " + result.Text);
                SshAssert.Contains(result.Text, "service: svc-openssh-server", "header");
                ToolResult bad = await Run(r, "ssh_service_status", Json(new { unit = "-bad;unit" }), token).ConfigureAwait(false);
                SshAssert.Error(bad, "invalid_argument", "invalid unit");
            }
        }

        private static async Task ServiceControl(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-svcctl"))
            {
                ToolResult result = await Run(r, "ssh_service_control", Json(new { unit = "hugo-no-such-unit", action = "restart" }), token).ConfigureAwait(false);
                SshAssert.True(result.IsError && (result.ErrorCode == "command_failed" || result.ErrorCode == "not_supported"), "failure reported: " + result.ErrorCode + " " + result.Text);
                SshAssert.Contains(result.Text, "hugo-no-such-unit", "names the unit");
                ToolResult sudo = await Run(r, "ssh_service_control", Json(new { unit = "nginx", action = "start", useSudo = true }), token).ConfigureAwait(false);
                SshAssert.Error(sudo, "rejected", "sudo without allowSudo");
            }
        }

        private static async Task ReadFileBytes(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-read"))
            {
                ToolResult all = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/data.txt" }), token).ConfigureAwait(false);
                SshAssert.Ok(all, "read");
                SshAssert.Contains(all.Text, "(23 bytes): bytes 0-23", "header");
                SshAssert.Contains(all.Text, "alpha\nbeta\ngamma\ndelta\n", "content");
                ToolResult part = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/data.txt", offset = 6, length = 4 }), token).ConfigureAwait(false);
                SshAssert.Ok(part, "range");
                SshAssert.Contains(part.Text, "bytes 6-10; more data follows, continue with offset=10", "range header");
                SshAssert.True(part.Text.EndsWith("\nbeta", StringComparison.Ordinal), "range content: " + part.Text);
                ToolResult past = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/data.txt", offset = 1000 }), token).ConfigureAwait(false);
                SshAssert.Error(past, "invalid_argument", "offset past end");
                ToolResult small = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/app.log" }), token, r.Invocation(maxBytes: 2048)).ConfigureAwait(false);
                SshAssert.Ok(small, "bounded read");
                SshAssert.True(small.Truncated && small.Text.Contains("continue with offset=", StringComparison.Ordinal), "bounded read pages");
            }
        }

        private static async Task ReadFileLines(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-read-lines"))
            {
                ToolResult result = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/app.log", startLine = 10, lineCount = 2 }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "read lines");
                SshAssert.Contains(result.Text, "lines 10-11 of 500; continue with startLine=12", "header");
                SshAssert.Contains(result.Text, "ERROR request failed line 10\n", "line 10");
                SshAssert.Contains(result.Text, "INFO request ok line 11\n", "line 11");
                SshAssert.NotContains(result.Text, "line 12\n", "line 12");
                ToolResult mixed = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/app.log", startLine = 1, offset = 5 }), token).ConfigureAwait(false);
                SshAssert.Error(mixed, "invalid_argument", "mixed modes");
            }
        }

        private static async Task ReadFileErrors(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-read-errors"))
            {
                ToolResult missing = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/nope.txt" }), token).ConfigureAwait(false);
                SshAssert.Error(missing, "not_found", "missing");
                ToolResult denied = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/private.txt" }), token).ConfigureAwait(false);
                SshAssert.Error(denied, "permission_denied", "private");
                ToolResult dir = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/dir" }), token).ConfigureAwait(false);
                SshAssert.Error(dir, "invalid_argument", "directory");
            }
        }

        private static async Task ReadFileBinary(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-read-binary"))
            {
                ToolResult result = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/binary.bin" }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "binary");
                SshAssert.Contains(result.Text, "binary content shown as base64", "header");
                string body = result.Text.Substring(result.Text.IndexOf('\n') + 1).Trim();
                SshAssert.True(Convert.FromBase64String(body).Length == 2048, "base64 decodes to the file");

                ToolResult lines = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/binary.bin", startLine = 1 }), token).ConfigureAwait(false);
                SshAssert.Error(lines, "invalid_argument", "binary in line mode");
                SshAssert.Contains(lines.Text, "offset and length", "line mode error points at byte mode");
            }
        }

        private static async Task TailFile(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-tail"))
            {
                ToolResult result = await Run(r, "ssh_tail_file", Json(new { path = "/srv/hugo/app.log", lines = 5 }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "tail");
                SshAssert.Contains(result.Text, "line 496\n", "first of last five");
                SshAssert.True(result.Text.EndsWith("ERROR request failed line 500\n", StringComparison.Ordinal), "last line");
                SshAssert.NotContains(result.Text, "line 495\n", "only five");
            }
        }

        private static async Task TailFileGrep(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-tail-grep"))
            {
                ToolResult result = await Run(r, "ssh_tail_file", Json(new { path = "/srv/hugo/app.log", lines = 3, grep = "error", ignoreCase = true }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "tail grep");
                SshAssert.Contains(result.Text, "line 480\n", "480");
                SshAssert.Contains(result.Text, "line 490\n", "490");
                SshAssert.Contains(result.Text, "line 500\n", "500");
                SshAssert.NotContains(result.Text, "INFO", "only matches");
                ToolResult none = await Run(r, "ssh_tail_file", Json(new { path = "/srv/hugo/app.log", grep = "no-such-text" }), token).ConfigureAwait(false);
                SshAssert.Ok(none, "no matches");
                SshAssert.Contains(none.Text, "(no matching lines)", "no matches");
            }
        }

        private static async Task TailFileNotFound(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-tail-missing"))
            {
                ToolResult missing = await Run(r, "ssh_tail_file", Json(new { path = "/srv/hugo/nope.log" }), token).ConfigureAwait(false);
                SshAssert.Error(missing, "not_found", "missing");
                ToolResult denied = await Run(r, "ssh_tail_file", Json(new { path = "/srv/hugo/private.txt" }), token).ConfigureAwait(false);
                SshAssert.Error(denied, "permission_denied", "private");
            }
        }

        private static async Task ListDirectory(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-list"))
            {
                ToolResult result = await Run(r, "ssh_list_directory", Json(new { path = "/srv/hugo/dir" }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "list");
                SshAssert.Contains(result.Text, "# /srv/hugo/dir: 4 entries", "header");
                SshAssert.Contains(result.Text, "| sub | dir |", "directory first");
                SshAssert.Contains(result.Text, "| a.txt | file | 2 | -rw-r--r-- |", "file row");
                SshAssert.NotContains(result.Text, ".hidden |", "hidden omitted");
                SshAssert.Contains(result.Text, "1 hidden entries omitted", "hidden note");
                SshAssert.True(result.Text.IndexOf("| sub |", StringComparison.Ordinal) < result.Text.IndexOf("| a.txt |", StringComparison.Ordinal), "order");

                ToolResult hidden = await Run(r, "ssh_list_directory", Json(new { path = "/srv/hugo/dir", includeHidden = true }), token).ConfigureAwait(false);
                SshAssert.Contains(hidden.Text, "| .hidden |", "hidden included");

                ToolResult capped = await Run(r, "ssh_list_directory", Json(new { path = "/srv/hugo/dir", limit = 2 }), token).ConfigureAwait(false);
                SshAssert.True(capped.Truncated && capped.Text.Contains("showing the first 2", StringComparison.Ordinal), "capped");
                SshAssert.True(capped.Metrics["rows"] == 2, "rows metric");
            }
        }

        private static async Task ListDirectoryNotFound(CancellationToken token)
        {
            using (SshTestResource r = PasswordResource("tb-list-missing"))
            {
                ToolResult result = await Run(r, "ssh_list_directory", Json(new { path = "/srv/hugo/no-such-dir" }), token).ConfigureAwait(false);
                SshAssert.Error(result, "not_found", "missing");
            }
        }

        private static async Task WriteFile(CancellationToken token)
        {
            string path = "/tmp/hugo-test-" + Guid.NewGuid().ToString("N") + ".txt";
            using (SshTestResource r = PasswordResource("tb-write"))
            {
                try
                {
                    ToolResult created = await Run(r, "ssh_write_file", Json(new { path = path, content = "first\n", mode = "0600" }), token).ConfigureAwait(false);
                    SshAssert.Ok(created, "create");
                    SshAssert.Contains(created.Text, "Created " + path + " (6 bytes, mode 0600)", "created");

                    ToolResult again = await Run(r, "ssh_write_file", Json(new { path = path, content = "second\n" }), token).ConfigureAwait(false);
                    SshAssert.Error(again, "already_exists", "refuses overwrite");

                    ToolResult over = await Run(r, "ssh_write_file", Json(new { path = path, content = "c2Vjb25kCg==", encoding = "base64", overwrite = true }), token).ConfigureAwait(false);
                    SshAssert.Ok(over, "overwrite");
                    SshAssert.Contains(over.Text, "Overwrote " + path, "overwrote");

                    ToolResult read = await Run(r, "ssh_read_file", Json(new { path = path }), token).ConfigureAwait(false);
                    SshAssert.True(read.Text.EndsWith("\nsecond\n", StringComparison.Ordinal), "content replaced: " + read.Text);
                    ToolResult list = await Run(r, "ssh_list_directory", Json(new { path = "/tmp" }), token).ConfigureAwait(false);
                    SshAssert.Contains(list.Text, "| -rw------- |", "mode applied");

                    ToolResult tooBig = await Run(r, "ssh_write_file", Json(new { path = path + ".big", content = new string('x', 2048) }), token, null).ConfigureAwait(false);
                    SshAssert.Ok(tooBig, "2 KiB write is within the default limit");
                }
                finally
                {
                    await Run(r, "ssh_exec", Json(new { command = "rm -f " + path + " " + path + ".big" }), CancellationToken.None).ConfigureAwait(false);
                }
            }
            using (SshTestResource limited = PasswordResource("tb-write-limit", new Dictionary<string, string> { { "maxFileWriteBytes", "10" } }))
            {
                ToolResult big = await Run(limited, "ssh_write_file", Json(new { path = path + ".nope", content = new string('x', 64) }), token).ConfigureAwait(false);
                SshAssert.Error(big, "invalid_argument", "write limit");
            }
        }

        private static async Task WriteFileRelative(CancellationToken token)
        {
            string name = "hugo-rel-" + Guid.NewGuid().ToString("N") + ".txt";
            using (SshTestResource r = PasswordResource("tb-write-rel", new Dictionary<string, string> { { "workingDirectory", "/tmp" } }))
            {
                try
                {
                    ToolResult created = await Run(r, "ssh_write_file", Json(new { path = name, content = "rel" }), token).ConfigureAwait(false);
                    SshAssert.Ok(created, "create relative");
                    SshAssert.Contains(created.Text, "/tmp/" + name, "resolved path");
                    ToolResult check = await Run(r, "ssh_exec", Json(new { command = "cat /tmp/" + name }), token).ConfigureAwait(false);
                    SshAssert.Contains(check.Text, "rel", "content");
                }
                finally
                {
                    await Run(r, "ssh_exec", Json(new { command = "rm -f /tmp/" + name }), CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static SshTestResource JumpResource(string name, Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> s = SshTestbed.PasswordSettings();
            s["host"] = SshTestbed.TargetFromBastion;
            s["port"] = SshTestbed.TargetPortFromBastion.ToString(CultureInfo.InvariantCulture);
            s["jumpHost"] = SshTestbed.Host;
            s["jumpPort"] = SshTestbed.BastionPort.ToString(CultureInfo.InvariantCulture);
            s["jumpUsername"] = SshTestbed.KeyUser;
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kv in extra) s[kv.Key] = kv.Value;
            }
            Dictionary<string, Dictionary<string, string>> secrets = SshTestbed.PasswordSecrets();
            secrets["jumpPrivateKey"] = new Dictionary<string, string> { { "privateKey", SshTestbed.ReadKey("id_ed25519") } };
            return new SshTestResource(name, s, secrets);
        }

        private static async Task JumpHost(CancellationToken token)
        {
            using (SshTestResource r = JumpResource("tb-jump"))
            {
                ConnectorValidationResult v = await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(v.IsValid, "validation: " + String.Join("; ", v.Errors.Select(e => e.Field + " " + e.Message)));
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(h.Success, "success: " + h.ErrorCode + " " + h.Message);
                SshAssert.True(h.Details.ContainsKey("jumpHostKeyFingerprint"), "jump fingerprint reported");
                SshAssert.True(h.Details["jumpHostKeyFingerprint"] != h.Details["hostKeyFingerprint"], "distinct host keys");
                SshAssert.True(r.Learned.ContainsKey("hostKeyFingerprint") && r.Learned.ContainsKey("jumpHostKeyFingerprint"), "both learned");

                ToolResult result = await Run(r, "ssh_exec", Json(new { command = "hostname; cat /srv/hugo/whoami.txt" }), token).ConfigureAwait(false);
                SshAssert.Ok(result, "exec via bastion");
                SshAssert.Contains(result.Text, "hugo-ssh-target", "landed on the target");
                SshAssert.NotContains(result.Text, "hugo-ssh-bastion", "not the bastion");
            }
        }

        private static async Task JumpHostSftp(CancellationToken token)
        {
            using (SshTestResource r = JumpResource("tb-jump-sftp"))
            {
                ToolResult list = await Run(r, "ssh_list_directory", Json(new { path = "/srv/hugo/dir" }), token).ConfigureAwait(false);
                SshAssert.Ok(list, "list via bastion");
                SshAssert.Contains(list.Text, "a.txt", "entries");
                ToolResult read = await Run(r, "ssh_read_file", Json(new { path = "/srv/hugo/whoami.txt" }), token).ConfigureAwait(false);
                SshAssert.Contains(read.Text, "hugo-ssh-target", "read via bastion");
            }
        }

        private static async Task JumpHostMismatch(CancellationToken token)
        {
            using (SshTestResource r = JumpResource("tb-jump-mismatch", new Dictionary<string, string> { { "jumpHostKeyFingerprint", _WrongFingerprint } }))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "host_key_mismatch", "health: " + h.ErrorCode + " " + h.Message);
                SshAssert.Contains(h.Message, "jump host", "names the bastion");
                SshAssert.True(r.Learned.IsEmpty, "nothing learned");
            }
        }

        #endregion
    }
}
