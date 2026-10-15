namespace Test.Shared.Connectors.Ssh
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Ssh;
    using Hugo.Connectors.Ssh.Classification;
    using Touchstone.Core;

    /// <summary>
    /// SSH connector unit cases (no network beyond 127.0.0.1 refusals): descriptor, schemas, validation, helpers,
    /// per-tool classification, and error mapping.
    /// </summary>
    public static class SshUnitTests
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "connectors.ssh";

        #endregion

        #region Private-Members

        private static readonly SshConnector _Connector = new SshConnector();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the unit cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "descriptor", "Descriptor has type, family, category, icon, slots, and instructions", Descriptor);
            Add(cases, "settings-schema", "Settings schema lists every documented key with defaults and ranges", SettingsSchema);
            Add(cases, "tools", "Tool set, names, base effects, and per-call flags", ToolsShape);
            Add(cases, "tool-schemas-parse", "Every tool input schema parses and every property is described", ToolSchemas);
            Add(cases, "validate-password-ok", "Password auth with a bound password validates", ValidatePasswordOk);
            Add(cases, "validate-password-missing", "Password auth without a password fails on the password slot", ValidatePasswordMissing);
            Add(cases, "validate-key-ok", "PrivateKey auth with a valid PEM key validates", ValidateKeyOk);
            Add(cases, "validate-key-garbage", "PrivateKey auth with an unreadable key fails with invalid_format", ValidateKeyGarbage);
            Add(cases, "validate-key-missing", "PrivateKey auth without a key fails on the privateKey slot", ValidateKeyMissing);
            Add(cases, "validate-fingerprint", "Host key fingerprints are format checked", ValidateFingerprint);
            Add(cases, "validate-patterns", "Invalid regular expressions in pattern settings are reported", ValidatePatterns);
            Add(cases, "validate-environment", "Environment entries must be NAME=value", ValidateEnvironment);
            Add(cases, "validate-jump", "Jump host needs a jump secret, and jump settings need a jump host", ValidateJump);
            Add(cases, "validate-misc", "Missing host, bad shell, and out-of-range timeout are reported", ValidateMisc);
            Add(cases, "options-defaults", "Options apply defaults and clamp ranges", OptionsDefaults);
            Add(cases, "options-resolve-path", "Relative paths resolve against the working directory", OptionsResolvePath);
            Add(cases, "fingerprint-normalize", "Fingerprints normalize with and without prefix and padding", FingerprintNormalize);
            Add(cases, "shell-quote", "Shell quoting and command building", ShellQuote);
            Add(cases, "parser-segments", "Parser splits lists, pipelines, substitutions, and redirections", ParserSegments);
            Add(cases, "tail-buffer", "Tail buffer keeps the end and counts the total", TailBuffer);
            Add(cases, "bound-tail-dropped", "Bounding reports the true total when capture dropped bytes", BoundTailDropped);
            Add(cases, "result-formatter", "Formatter reports exit code, stdout, and stderr separately with structured JSON", ResultFormatter);
            Add(cases, "classify-exec", "ssh_exec classifies per call with an approval summary", ClassifyExec);
            Add(cases, "classify-write-file", "ssh_write_file is Write, or Destructive with overwrite=true", ClassifyWriteFile);
            Add(cases, "classify-service-control", "ssh_service_control is Destructive and rejects sudo when not allowed", ClassifyServiceControl);
            Add(cases, "classify-read-tools", "Read tools classify as Read", ClassifyReadTools);
            Add(cases, "exec-rejected-without-connecting", "A rejected command returns rejected without connecting", ExecRejected);
            Add(cases, "exec-connection-refused", "A refused connection maps to connection_failed", ExecConnectionRefused);
            Add(cases, "test-connection-refused", "TestConnectionAsync reports failure without throwing", TestConnectionRefused);
            Add(cases, "exec-cancelled-before-start", "A cancelled token throws OperationCanceledException", ExecCancelled);
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

        private static Task Descriptor(CancellationToken token)
        {
            ConnectorDescriptor d = _Connector.Descriptor;
            SshAssert.True(d.Type == "ssh", "type");
            SshAssert.True(d.Family == "ssh", "family");
            SshAssert.True(d.Category == ConnectorCategoryEnum.Compute, "category");
            SshAssert.True(d.IconKey == "terminal", "icon");
            SshAssert.True(d.DefaultInstructionsKey == "connector.ssh.instructions", "instructions key");
            SshAssert.Contains(d.DefaultInstructions, "ssh_system_info", "instructions");
            SshAssert.Contains(d.DefaultInstructions, "host_key_mismatch", "instructions");
            SshAssert.NotContains(d.DefaultInstructions, "\u2014", "instructions (em-dash)");
            SshAssert.True(d.SecretSlots.Any(s => s.Name == "password" && s.AcceptedKinds.Contains(SecretKindEnum.Password)), "password slot");
            SshAssert.True(d.SecretSlots.Any(s => s.Name == "privateKey" && s.AcceptedKinds.Contains(SecretKindEnum.PrivateKey)), "privateKey slot");
            SshAssert.True(d.SecretSlots.Any(s => s.Name == "jumpPrivateKey") && d.SecretSlots.Any(s => s.Name == "jumpPassword"), "jump slots");
            SshAssert.True(d.SecretSlots.All(s => !s.Required), "slots optional (cross-slot rules in ValidateAsync)");
            return Task.CompletedTask;
        }

        private static Task SettingsSchema(CancellationToken token)
        {
            ConnectorSettingsSchema s = _Connector.Descriptor.SettingsSchema;
            string[] keys = new[] { "host", "port", "username", "authMethod", "hostKeyFingerprint", "jumpHost", "jumpPort", "jumpUsername", "jumpHostKeyFingerprint", "connectTimeoutSeconds", "commandTimeoutSeconds", "maxOutputBytes", "workingDirectory", "allowSudo", "shell", "readOnlyCommandPatterns", "deniedCommandPatterns", "destructiveCommandPatterns", "environment", "maxFileReadBytes", "maxFileWriteBytes", "keepAliveSeconds" };
            foreach (string key in keys) SshAssert.True(s.Find(key) != null, "setting " + key);
            SshAssert.True(s.Find("port")!.DefaultValue == "22", "port default");
            SshAssert.True(s.Find("commandTimeoutSeconds")!.DefaultValue == "60" && s.Find("commandTimeoutSeconds")!.Maximum == 900, "command timeout");
            SshAssert.True(s.Find("connectTimeoutSeconds")!.DefaultValue == "10", "connect timeout");
            SshAssert.True(s.Find("maxOutputBytes")!.DefaultValue == "65536", "max output");
            SshAssert.True(s.Find("maxFileReadBytes")!.DefaultValue == "1048576", "max file read");
            SshAssert.True(s.Find("allowSudo")!.DefaultValue == "false", "sudo default");
            SshAssert.True(s.Find("shell")!.DefaultValue == "/bin/bash -lc", "shell default");
            SshAssert.True(s.Find("authMethod")!.AllowedValues.SequenceEqual(new[] { "Password", "PrivateKey" }), "auth methods");
            SshAssert.True(s.Find("host")!.Required && s.Find("username")!.Required, "required");
            SshAssert.True(s.Settings.All(x => !String.IsNullOrEmpty(x.Help)), "every setting has help");
            return Task.CompletedTask;
        }

        private static Task ToolsShape(CancellationToken token)
        {
            Dictionary<string, EffectEnum> expected = new Dictionary<string, EffectEnum>
            {
                { "ssh_exec", EffectEnum.Read },
                { "ssh_system_info", EffectEnum.Read },
                { "ssh_service_status", EffectEnum.Read },
                { "ssh_service_control", EffectEnum.Destructive },
                { "ssh_read_file", EffectEnum.Read },
                { "ssh_tail_file", EffectEnum.Read },
                { "ssh_list_directory", EffectEnum.Read },
                { "ssh_write_file", EffectEnum.Write }
            };
            SshAssert.True(_Connector.Tools.Count == expected.Count, "tool count");
            foreach (KeyValuePair<string, EffectEnum> kv in expected)
            {
                ToolDescriptor d = Tool(kv.Key).Descriptor;
                SshAssert.True(d.BaseEffect == kv.Value, kv.Key + " base effect");
                SshAssert.True(d.Description.Length > 80, kv.Key + " description");
                SshAssert.True(!String.IsNullOrEmpty(d.Title), kv.Key + " title");
            }
            SshAssert.True(Tool("ssh_exec").Descriptor.ClassifiedPerCall, "exec per call");
            SshAssert.True(Tool("ssh_write_file").Descriptor.ClassifiedPerCall, "write per call");
            SshAssert.True(!Tool("ssh_read_file").Descriptor.ClassifiedPerCall, "read fixed");
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

        private static Dictionary<string, string> BaseSettings(string method)
        {
            return new Dictionary<string, string> { { "host", "web-01.example.com" }, { "username", "deploy" }, { "authMethod", method } };
        }

        private static async Task<ConnectorValidationResult> Validate(Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets, CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("validate", settings, secrets))
            {
                return await _Connector.ValidateAsync(r.Context, token).ConfigureAwait(false);
            }
        }

        private static string RsaPem()
        {
            using (RSA rsa = RSA.Create(2048))
            {
                return rsa.ExportRSAPrivateKeyPem();
            }
        }

        private static async Task ValidatePasswordOk(CancellationToken token)
        {
            ConnectorValidationResult r = await Validate(BaseSettings("Password"), SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.IsValid, "valid: " + String.Join("; ", r.Errors.Select(e => e.Field + " " + e.Message)));
        }

        private static async Task ValidatePasswordMissing(CancellationToken token)
        {
            ConnectorValidationResult r = await Validate(BaseSettings("Password"), null, token).ConfigureAwait(false);
            SshAssert.True(r.Errors.Any(e => e.Field == "password" && e.Code == "required"), "password required");
        }

        private static async Task ValidateKeyOk(CancellationToken token)
        {
            Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>
            {
                { "privateKey", new Dictionary<string, string> { { "privateKey", RsaPem() } } }
            };
            ConnectorValidationResult r = await Validate(BaseSettings("PrivateKey"), secrets, token).ConfigureAwait(false);
            SshAssert.True(r.IsValid, "valid: " + String.Join("; ", r.Errors.Select(e => e.Field + " " + e.Message)));
        }

        private static async Task ValidateKeyGarbage(CancellationToken token)
        {
            string garbage = "-----BEGIN OPENSSH PRIVATE KEY-----\nnot-a-key-at-all\n-----END OPENSSH PRIVATE KEY-----\n";
            Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>
            {
                { "privateKey", new Dictionary<string, string> { { "privateKey", garbage } } }
            };
            ConnectorValidationResult r = await Validate(BaseSettings("PrivateKey"), secrets, token).ConfigureAwait(false);
            ConnectorValidationError? e = r.Errors.FirstOrDefault(x => x.Field == "privateKey");
            SshAssert.True(e != null && e.Code == "invalid_format", "invalid key");
            SshAssert.NotContains(e!.Message, "not-a-key-at-all", "message must not echo key material");
        }

        private static async Task ValidateKeyMissing(CancellationToken token)
        {
            ConnectorValidationResult r = await Validate(BaseSettings("PrivateKey"), SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.Errors.Any(e => e.Field == "privateKey" && e.Code == "required"), "key required");
        }

        private static async Task ValidateFingerprint(CancellationToken token)
        {
            Dictionary<string, string> s = BaseSettings("Password");
            s["hostKeyFingerprint"] = "MD5:aa:bb";
            ConnectorValidationResult bad = await Validate(s, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(bad.Errors.Any(e => e.Field == "hostKeyFingerprint" && e.Code == "invalid_format"), "bad fingerprint");
            s["hostKeyFingerprint"] = "SHA256:" + new string('Q', 43);
            ConnectorValidationResult good = await Validate(s, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(good.IsValid, "good fingerprint");
        }

        private static async Task ValidatePatterns(CancellationToken token)
        {
            Dictionary<string, string> s = BaseSettings("Password");
            s["deniedCommandPatterns"] = "rm\n([unclosed";
            s["readOnlyCommandPatterns"] = "myapp status";
            ConnectorValidationResult r = await Validate(s, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.Errors.Count == 1 && r.Errors[0].Field == "deniedCommandPatterns" && r.Errors[0].Code == "invalid_format", "bad regex");
        }

        private static async Task ValidateEnvironment(CancellationToken token)
        {
            Dictionary<string, string> s = BaseSettings("Password");
            s["environment"] = "GOOD=1\n1BAD=2";
            ConnectorValidationResult r = await Validate(s, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.Errors.Any(e => e.Field == "environment"), "bad env");
            s["environment"] = "GOOD=1\nLANG=C.UTF-8";
            r = await Validate(s, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.IsValid, "good env");
        }

        private static async Task ValidateJump(CancellationToken token)
        {
            Dictionary<string, string> s = BaseSettings("Password");
            s["jumpHost"] = "bastion.example.com";
            ConnectorValidationResult r = await Validate(s, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.Errors.Any(e => e.Field == "jumpPrivateKey" && e.Code == "required"), "jump secret required");

            Dictionary<string, Dictionary<string, string>> secrets = SshTestbed.PasswordSecrets("pw");
            secrets["jumpPassword"] = new Dictionary<string, string> { { "password", "jpw" } };
            r = await Validate(s, secrets, token).ConfigureAwait(false);
            SshAssert.True(r.IsValid, "jump with password valid");

            Dictionary<string, string> s2 = BaseSettings("Password");
            s2["jumpUsername"] = "jump";
            r = await Validate(s2, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.Errors.Any(e => e.Field == "jumpHost" && e.Code == "required"), "jump host required");
        }

        private static async Task ValidateMisc(CancellationToken token)
        {
            Dictionary<string, string> s = new Dictionary<string, string> { { "username", "deploy" }, { "authMethod", "Password" }, { "shell", "/bin/bash -lc; rm -rf /" }, { "commandTimeoutSeconds", "901" } };
            ConnectorValidationResult r = await Validate(s, SshTestbed.PasswordSecrets("pw"), token).ConfigureAwait(false);
            SshAssert.True(r.Errors.Any(e => e.Field == "host" && e.Code == "required"), "host required");
            SshAssert.True(r.Errors.Any(e => e.Field == "shell" && e.Code == "invalid_format"), "shell");
            SshAssert.True(r.Errors.Any(e => e.Field == "commandTimeoutSeconds" && e.Code == "out_of_range"), "timeout range");
        }

        private static Task OptionsDefaults(CancellationToken token)
        {
            SshConnectionOptions o = SshConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "host", " h " }, { "username", "u" }, { "commandTimeoutSeconds", "5000" }, { "maxOutputBytes", "10" } }));
            SshAssert.True(o.Host == "h" && o.Port == 22, "host and port");
            SshAssert.True(o.CommandTimeoutSeconds == 900, "timeout clamped");
            SshAssert.True(o.MaxOutputBytes == 1024, "output clamped");
            SshAssert.True(o.ConnectTimeoutSeconds == 10 && o.KeepAliveSeconds == 30, "connect and keepalive defaults");
            SshAssert.True(o.Shell == "/bin/bash -lc" && !o.AllowSudo && !o.UsesJumpHost, "shell, sudo, jump defaults");
            SshAssert.True(o.MaxFileReadBytes == 1048576 && o.MaxFileWriteBytes == 1048576, "file defaults");
            SshConnectionOptions empty = SshConnectionOptions.FromSettings(new ResourceSettings(new Dictionary<string, string> { { "shell", "" } }));
            SshAssert.True(empty.Shell == "/bin/bash -lc", "empty shell string means default (not set)");
            return Task.CompletedTask;
        }

        private static Task OptionsResolvePath(CancellationToken token)
        {
            SshConnectionOptions o = new SshConnectionOptions { WorkingDirectory = "/srv/app/" };
            SshAssert.True(o.ResolvePath("logs/a.log") == "/srv/app/logs/a.log", "relative");
            SshAssert.True(o.ResolvePath("/etc/hosts") == "/etc/hosts", "absolute");
            SshAssert.True(o.ResolvePath("~/x") == "~/x", "tilde");
            bool threw = false;
            try
            {
                o.ResolvePath("a\nb");
            }
            catch (ConnectorException e) when (e.Code == "invalid_argument")
            {
                threw = true;
            }
            SshAssert.True(threw, "newline rejected");
            return Task.CompletedTask;
        }

        private static Task FingerprintNormalize(CancellationToken token)
        {
            string body = "ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og";
            SshAssert.True(SshFingerprint.Normalize(body) == "SHA256:" + body, "bare");
            SshAssert.True(SshFingerprint.Normalize("sha256:" + body + "=") == "SHA256:" + body, "prefix and padding");
            SshAssert.True(SshFingerprint.Normalize("  ") == null && SshFingerprint.Normalize("SHA256:short") == null, "invalid");
            SshAssert.True(SshFingerprint.AreEqual(body, "SHA256:" + body), "equal");
            SshAssert.True(!SshFingerprint.AreEqual(body, "SHA256:" + body.ToLowerInvariant()), "case sensitive");
            SshHostKeyCheck tofu = new SshHostKeyCheck(null);
            SshAssert.True(tofu.Expected == null && !tofu.Mismatch, "tofu check");
            return Task.CompletedTask;
        }

        private static Task ShellQuote(CancellationToken token)
        {
            SshAssert.True(SshShell.Quote("it's") == "'it'\\''s'", "quote");
            SshAssert.True(SshShell.QuotePath("~/a b") == "\"$HOME\"/'a b'", "quote path");
            string cmd = SshShell.BuildCommand("echo 'hi'", "/bin/bash -lc", "/srv/my app", new[] { "A=1", "bad entry", "B=two words" });
            SshAssert.True(cmd == "export 'A=1'; export 'B=two words'; cd '/srv/my app' && /bin/bash -lc 'echo '\\''hi'\\'''", "built: " + cmd);
            SshAssert.True(SshShell.BuildCommand("ls", "", null, null) == "ls", "raw");
            SshAssert.True(SshShell.PosixScript("echo 'x'") == "/bin/sh -c 'echo '\\''x'\\'''", "posix");
            return Task.CompletedTask;
        }

        private static Task ParserSegments(CancellationToken token)
        {
            SshParseResult r = SshCommandParser.Parse("a x | b && c; d 2>&1 > out.txt");
            SshAssert.True(r.Success && r.Segments.Count == 4, "segments");
            SshAssert.True(r.Segments[3].Redirections.Count == 2 && r.Segments[3].Redirections[0].FileDescriptor == 2 && r.Segments[3].Redirections[0].Operator == ">&" && r.Segments[3].Redirections[1].Target == "out.txt", "redirections");
            SshParseResult s = SshCommandParser.Parse("echo \"$(id -u)\" `date` <(ls)");
            SshAssert.True(s.Success && s.Substitutions.Count == 3 && s.Substitutions[0] == "id -u" && s.Substitutions[2] == "ls", "substitutions");
            SshAssert.True(!SshCommandParser.Parse("cat <<EOF\nx\nEOF").Success, "heredoc fails");
            SshAssert.True(!SshCommandParser.Parse("echo \"open").Success, "unterminated fails");
            SshParseResult q = SshCommandParser.Parse("grep 'a b' \"c d\" e\\ f");
            SshAssert.True(q.Segments[0].Words.SequenceEqual(new[] { "grep", "a b", "c d", "e f" }), "quoting");
            return Task.CompletedTask;
        }

        private static Task TailBuffer(CancellationToken token)
        {
            SshTailBuffer b = new SshTailBuffer(4);
            byte[] data = Encoding.ASCII.GetBytes("0123456789");
            b.Write(data, 0, 3);
            b.Write(data, 3, 7);
            SshAssert.True(Encoding.ASCII.GetString(b.ToArray()) == "6789", "tail");
            SshAssert.True(b.TotalBytes == 10 && b.Dropped, "total");
            SshTailBuffer c = new SshTailBuffer(16);
            c.Write(data, 0, 5);
            SshAssert.True(c.ToString() == "01234" && !c.Dropped, "small");
            return Task.CompletedTask;
        }

        private static Task BoundTailDropped(CancellationToken token)
        {
            ToolResult r = new ToolResult();
            string text = SshOutput.BoundTail("line1\nline2\n", 1000000, true, 4096, 100, r);
            SshAssert.True(text.StartsWith("[output truncated: 1000000 bytes total", StringComparison.Ordinal), "marker: " + text);
            SshAssert.True(text.EndsWith("line2\n", StringComparison.Ordinal), "keeps end");
            SshAssert.True(r.Truncated && r.OriginalBytes == 1000000, "fields");
            ToolResult r2 = new ToolResult();
            string kept = SshOutput.BoundTail("a\nb\nc\n", 6, false, 4096, 2, r2);
            SshAssert.True(kept.EndsWith("b\nc\n", StringComparison.Ordinal) && r2.Truncated, "line limit");
            return Task.CompletedTask;
        }

        private static Task ResultFormatter(CancellationToken token)
        {
            SshCommandResult c = new SshCommandResult { ExitCode = 3, Stdout = "out\n", Stderr = "err\n", StdoutBytes = 4, StderrBytes = 4, DurationMs = 12 };
            ToolResult r = SshResultFormatter.Build(c, 65536, 1000, null);
            SshAssert.True(!r.IsError, "non-zero exit is not an error");
            SshAssert.Contains(r.Text, "exit_code: 3", "exit code");
            SshAssert.Contains(r.Text, "--- stdout (4 bytes) ---\nout\n", "stdout");
            SshAssert.Contains(r.Text, "--- stderr (4 bytes) ---\nerr\n", "stderr");
            SshExecOutput? o = JsonSerializer.Deserialize<SshExecOutput>(r.StructuredJson ?? "{}");
            SshAssert.True(o != null && o.ExitCode == 3 && o.Stdout == "out\n" && o.Stderr == "err\n", "structured");
            SshAssert.True(r.Metrics["exitCode"] == 3, "metrics");

            string big = String.Join("\n", Enumerable.Range(1, 5000)) + "\n";
            SshCommandResult b = new SshCommandResult { ExitCode = 0, Stdout = big, StdoutBytes = Encoding.UTF8.GetByteCount(big) };
            ToolResult rb = SshResultFormatter.Build(b, 4096, 1000, null);
            SshAssert.True(rb.Truncated && Encoding.UTF8.GetByteCount(rb.Text) <= 4096 + 600, "bounded: " + Encoding.UTF8.GetByteCount(rb.Text));
            SshAssert.Contains(rb.Text, "5000\n", "keeps end");
            return Task.CompletedTask;
        }

        private static Task ClassifyExec(CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("web-01", BaseSettings("Password"), null))
            {
                ConnectorInvocationContext ctx = r.Invocation();
                IConnectorTool exec = Tool("ssh_exec");
                EffectClassification read = exec.Classify(ctx, new ToolArguments("{\"command\":\"df -h\"}"));
                SshAssert.True(read.Effect == EffectEnum.Read && !read.Rejected, "read");
                SshAssert.True(read.ApprovalSummary == "Run \"df -h\" on web-01", "summary: " + read.ApprovalSummary);
                EffectClassification destructive = exec.Classify(ctx, new ToolArguments("{\"command\":\"systemctl restart nginx\"}"));
                SshAssert.True(destructive.Effect == EffectEnum.Destructive, "destructive");
                SshAssert.Contains(destructive.Reason, "systemctl restart", "reason");
                EffectClassification missing = exec.Classify(ctx, new ToolArguments("{}"));
                SshAssert.True(missing.Effect == EffectEnum.Destructive, "missing command fails closed");
                EffectClassification sudo = exec.Classify(ctx, new ToolArguments("{\"command\":\"sudo ls\"}"));
                SshAssert.True(sudo.Rejected, "sudo rejected");
            }
            Dictionary<string, string> s = BaseSettings("Password");
            s["allowSudo"] = "true";
            s["deniedCommandPatterns"] = "docker";
            using (SshTestResource r = new SshTestResource("web-02", s, null))
            {
                IConnectorTool exec = Tool("ssh_exec");
                SshAssert.True(exec.Classify(r.Invocation(), new ToolArguments("{\"command\":\"sudo -n ls\"}")).Effect == EffectEnum.Read, "sudo allowed");
                SshAssert.True(exec.Classify(r.Invocation(), new ToolArguments("{\"command\":\"docker ps\"}")).Rejected, "deny pattern from settings");
            }
            return Task.CompletedTask;
        }

        private static Task ClassifyWriteFile(CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("web-01", BaseSettings("Password"), null))
            {
                IConnectorTool write = Tool("ssh_write_file");
                EffectClassification create = write.Classify(r.Invocation(), new ToolArguments("{\"path\":\"/tmp/a\",\"content\":\"x\"}"));
                SshAssert.True(create.Effect == EffectEnum.Write, "create is Write");
                EffectClassification overwrite = write.Classify(r.Invocation(), new ToolArguments("{\"path\":\"/tmp/a\",\"content\":\"x\",\"overwrite\":true}"));
                SshAssert.True(overwrite.Effect == EffectEnum.Destructive, "overwrite is Destructive");
                SshAssert.Contains(overwrite.ApprovalSummary, "/tmp/a", "summary");
            }
            return Task.CompletedTask;
        }

        private static Task ClassifyServiceControl(CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("web-01", BaseSettings("Password"), null))
            {
                IConnectorTool control = Tool("ssh_service_control");
                EffectClassification c = control.Classify(r.Invocation(), new ToolArguments("{\"unit\":\"nginx\",\"action\":\"restart\"}"));
                SshAssert.True(c.Effect == EffectEnum.Destructive && !c.Rejected, "destructive");
                SshAssert.True(c.ApprovalSummary == "Restart service nginx on web-01", "summary: " + c.ApprovalSummary);
                EffectClassification s = control.Classify(r.Invocation(), new ToolArguments("{\"unit\":\"nginx\",\"action\":\"start\",\"useSudo\":true}"));
                SshAssert.True(s.Rejected, "sudo rejected");
            }
            return Task.CompletedTask;
        }

        private static Task ClassifyReadTools(CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("web-01", BaseSettings("Password"), null))
            {
                foreach (string name in new[] { "ssh_system_info", "ssh_service_status", "ssh_read_file", "ssh_tail_file", "ssh_list_directory" })
                {
                    EffectClassification c = Tool(name).Classify(r.Invocation(), new ToolArguments("{\"path\":\"/etc/hosts\",\"unit\":\"nginx\"}"));
                    SshAssert.True(c.Effect == EffectEnum.Read && !c.Rejected, name);
                }
            }
            return Task.CompletedTask;
        }

        private static Dictionary<string, string> RefusedSettings()
        {
            Dictionary<string, string> s = BaseSettings("Password");
            s["host"] = "127.0.0.1";
            s["port"] = "1";
            s["connectTimeoutSeconds"] = "3";
            return s;
        }

        private static async Task ExecRejected(CancellationToken token)
        {
            Dictionary<string, string> s = RefusedSettings();
            s["deniedCommandPatterns"] = "uptime";
            using (SshTestResource r = new SshTestResource("refused-deny", s, SshTestbed.PasswordSecrets("pw")))
            {
                ToolResult result = await Tool("ssh_exec").ExecuteAsync(r.Invocation(), new ToolArguments("{\"command\":\"uptime\"}"), token).ConfigureAwait(false);
                SshAssert.Error(result, "rejected", "exec denied");
            }
        }

        private static async Task ExecConnectionRefused(CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("refused", RefusedSettings(), SshTestbed.PasswordSecrets("pw")))
            {
                Stopwatch sw = Stopwatch.StartNew();
                ToolResult result = await Tool("ssh_exec").ExecuteAsync(r.Invocation(), new ToolArguments("{\"command\":\"uptime\"}"), token).ConfigureAwait(false);
                SshAssert.Error(result, "connection_failed", "exec refused");
                SshAssert.True(sw.Elapsed < TimeSpan.FromSeconds(10), "fast failure");
                SshAssert.NotContains(result.Text, "pw", "no secret in message");
                ToolResult list = await Tool("ssh_list_directory").ExecuteAsync(r.Invocation(), new ToolArguments("{}"), token).ConfigureAwait(false);
                SshAssert.Error(list, "connection_failed", "sftp refused");
            }
        }

        private static async Task TestConnectionRefused(CancellationToken token)
        {
            using (SshTestResource r = new SshTestResource("refused-test", RefusedSettings(), SshTestbed.PasswordSecrets("pw")))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "connection_failed", "health: " + h.ErrorCode + " " + h.Message);
            }
            Dictionary<string, string> noSecret = RefusedSettings();
            using (SshTestResource r = new SshTestResource("refused-nosecret", noSecret, null))
            {
                ConnectorHealthResult h = await _Connector.TestConnectionAsync(r.Context, token).ConfigureAwait(false);
                SshAssert.True(!h.Success && h.ErrorCode == "auth_failed", "missing secret: " + h.ErrorCode + " " + h.Message);
            }
        }

        private static async Task ExecCancelled(CancellationToken token)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            using (SshTestResource r = new SshTestResource("cancelled", RefusedSettings(), SshTestbed.PasswordSecrets("pw")))
            {
                cts.Cancel();
                bool threw = false;
                try
                {
                    await Tool("ssh_exec").ExecuteAsync(r.Invocation(), new ToolArguments("{\"command\":\"uptime\"}"), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
                SshAssert.True(threw, "cancelled");
            }
        }

        #endregion
    }
}
