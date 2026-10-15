namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Ssh.Tools;

    /// <summary>
    /// The <c>ssh</c> connector: commands, SFTP file access, and service management on Linux and Unix hosts over SSH.
    /// Thread-safe; one instance serves every resource.
    /// </summary>
    public class SshConnector : IConnector
    {
        #region Public-Members

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new SshSystemInfoTool(),
                new SshExecTool(),
                new SshServiceStatusTool(),
                new SshServiceControlTool(),
                new SshReadFileTool(),
                new SshTailFileTool(),
                new SshListDirectoryTool(),
                new SshWriteFileTool()
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

            string host = s.GetString(SshSettingKeys.Host) ?? String.Empty;
            if (String.IsNullOrWhiteSpace(host)) result.Add(SshSettingKeys.Host, "required", "A host is required.");
            else if (host.Trim().IndexOfAny(new char[] { ' ', '\t', '\n', '\r', '\0', '/' }) >= 0) result.Add(SshSettingKeys.Host, "invalid_format", "The host must be a host name or IP address without spaces or slashes.");

            if (String.IsNullOrWhiteSpace(s.GetString(SshSettingKeys.Username))) result.Add(SshSettingKeys.Username, "required", "A username is required.");

            string method = s.GetString(SshSettingKeys.AuthMethod, SshSettingKeys.AuthMethodPrivateKey) ?? SshSettingKeys.AuthMethodPrivateKey;
            if (String.Equals(method, SshSettingKeys.AuthMethodPassword, StringComparison.OrdinalIgnoreCase))
            {
                if (String.IsNullOrEmpty(resource.Secrets.Get(SshSettingKeys.PasswordSlot, SshSettingKeys.PasswordField)))
                    result.Add(SshSettingKeys.PasswordSlot, "required", "authMethod is Password, so bind a Password secret to the 'password' slot.");
            }
            else if (String.Equals(method, SshSettingKeys.AuthMethodPrivateKey, StringComparison.OrdinalIgnoreCase))
            {
                string? key = resource.Secrets.Get(SshSettingKeys.PrivateKeySlot, SshSettingKeys.PrivateKeyField);
                if (String.IsNullOrWhiteSpace(key)) result.Add(SshSettingKeys.PrivateKeySlot, "required", "authMethod is PrivateKey, so bind a PrivateKey secret to the 'privateKey' slot.");
                else if (!SshCredential.TryValidateKey(key, resource.Secrets.Get(SshSettingKeys.PrivateKeySlot, SshSettingKeys.PassphraseField), out string error))
                    result.Add(SshSettingKeys.PrivateKeySlot, "invalid_format", error);
            }
            else
            {
                result.Add(SshSettingKeys.AuthMethod, "invalid_value", "authMethod must be Password or PrivateKey.");
            }

            ValidateFingerprint(result, s, SshSettingKeys.HostKeyFingerprint);
            ValidateFingerprint(result, s, SshSettingKeys.JumpHostKeyFingerprint);

            if (s.Has(SshSettingKeys.JumpHost))
            {
                string jumpHost = s.GetString(SshSettingKeys.JumpHost) ?? String.Empty;
                if (jumpHost.Trim().IndexOfAny(new char[] { ' ', '\t', '\n', '\r', '\0', '/' }) >= 0) result.Add(SshSettingKeys.JumpHost, "invalid_format", "The jump host must be a host name or IP address without spaces or slashes.");
                string? jumpKey = resource.Secrets.Get(SshSettingKeys.JumpPrivateKeySlot, SshSettingKeys.PrivateKeyField);
                string? jumpPassword = resource.Secrets.Get(SshSettingKeys.JumpPasswordSlot, SshSettingKeys.PasswordField);
                if (String.IsNullOrWhiteSpace(jumpKey) && String.IsNullOrEmpty(jumpPassword))
                    result.Add(SshSettingKeys.JumpPrivateKeySlot, "required", "jumpHost is set, so bind a secret to the 'jumpPrivateKey' or 'jumpPassword' slot.");
                else if (!String.IsNullOrWhiteSpace(jumpKey) && !SshCredential.TryValidateKey(jumpKey, resource.Secrets.Get(SshSettingKeys.JumpPrivateKeySlot, SshSettingKeys.PassphraseField), out string jumpError))
                    result.Add(SshSettingKeys.JumpPrivateKeySlot, "invalid_format", jumpError);
            }
            else if (s.Has(SshSettingKeys.JumpUsername) || s.Has(SshSettingKeys.JumpHostKeyFingerprint))
            {
                result.Add(SshSettingKeys.JumpHost, "required", "jumpUsername or jumpHostKeyFingerprint is set, but jumpHost is empty.");
            }

            long timeout = s.GetLong(SshSettingKeys.CommandTimeoutSeconds, 60);
            if (timeout < 1 || timeout > 900) result.Add(SshSettingKeys.CommandTimeoutSeconds, "out_of_range", "commandTimeoutSeconds must be between 1 and 900.");

            string? workingDirectory = s.GetString(SshSettingKeys.WorkingDirectory);
            if (workingDirectory != null && (workingDirectory.IndexOf('\n') >= 0 || workingDirectory.IndexOf('\0') >= 0))
                result.Add(SshSettingKeys.WorkingDirectory, "invalid_format", "The working directory must not contain newline or NUL characters.");

            string? shell = s.GetString(SshSettingKeys.Shell);
            if (shell != null && (shell.IndexOf('\n') >= 0 || shell.IndexOf('\0') >= 0 || shell.IndexOf(';') >= 0 || shell.IndexOf('&') >= 0 || shell.IndexOf('|') >= 0))
                result.Add(SshSettingKeys.Shell, "invalid_format", "The shell must be a program and its options, for example /bin/bash -lc, without ; & | or newlines.");

            foreach (string entry in s.GetStringList(SshSettingKeys.Environment))
            {
                if (!SshShell.IsValidEnvironmentEntry(entry))
                {
                    result.Add(SshSettingKeys.Environment, "invalid_format", "Each environment entry must look like NAME=value with a letter or underscore first.");
                    break;
                }
            }

            ValidatePatterns(result, s, SshSettingKeys.ReadOnlyCommandPatterns);
            ValidatePatterns(result, s, SshSettingKeys.DestructiveCommandPatterns);
            ValidatePatterns(result, s, SshSettingKeys.DeniedCommandPatterns);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            SshConnectionOptions options = SshConnectionOptions.FromSettings(resource.Settings);
            try
            {
                using (SshSession session = await SshSessionFactory.ConnectAsync(resource, options, token).ConfigureAwait(false))
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(Math.Min(options.CommandTimeoutSeconds, 20));
                    SshCommandResult probe = await SshCommandRunner.RunAsync(session.Client, "uname -a", timeout, 4096, token).ConfigureAwait(false);
                    sw.Stop();
                    string uname = probe.Stdout.Trim();
                    ConnectorHealthResult health = probe.TimedOut
                        ? ConnectorHealthResult.Fail("timeout", "Connected, but the probe command did not finish within " + (int)timeout.TotalSeconds + " seconds.", sw.Elapsed.TotalMilliseconds)
                        : ConnectorHealthResult.Ok("Connected to " + options.Host + ":" + options.Port + " as " + options.Username + (String.IsNullOrEmpty(uname) ? String.Empty : " (" + Shorten(uname, 160) + ")"), sw.Elapsed.TotalMilliseconds);
                    health.Details["hostKeyFingerprint"] = session.HostKeyFingerprint;
                    health.Details["hostKeyAlgorithm"] = session.HostKeyAlgorithm;
                    health.Details["hostKeyPinned"] = options.HostKeyFingerprint != null ? "true" : "learned";
                    health.Details["serverVersion"] = session.ServerVersion;
                    if (!String.IsNullOrEmpty(uname)) health.Details["uname"] = Shorten(uname, 500);
                    if (session.JumpHostKeyFingerprint != null) health.Details["jumpHostKeyFingerprint"] = session.JumpHostKeyFingerprint;
                    return health;
                }
            }
            catch (ConnectorException e)
            {
                sw.Stop();
                return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                sw.Stop();
                ConnectorException mapped = SshErrorMapper.MapOperation(e, "connection test");
                return ConnectorHealthResult.Fail(mapped.Code, mapped.Message, sw.Elapsed.TotalMilliseconds);
            }
        }

        #endregion

        #region Private-Methods

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor(SshSettingKeys.Host, SettingValueKindEnum.String, "Host") { Required = true, Group = "Connection", ShowInSummary = true, Help = "Host name or IP address of the server." });
            schema.Add(new SettingDescriptor(SshSettingKeys.Port, SettingValueKindEnum.Integer, "Port") { DefaultValue = "22", Minimum = 1, Maximum = 65535, Group = "Connection", Help = "SSH port. Default 22." });
            schema.Add(new SettingDescriptor(SshSettingKeys.Username, SettingValueKindEnum.String, "Username") { Required = true, Group = "Connection", ShowInSummary = true, Help = "Login user. Use a dedicated, least-privilege account." });
            schema.Add(new SettingDescriptor(SshSettingKeys.AuthMethod, SettingValueKindEnum.Enum, "Authentication method") { Required = true, DefaultValue = SshSettingKeys.AuthMethodPrivateKey, AllowedValues = new List<string> { SshSettingKeys.AuthMethodPassword, SshSettingKeys.AuthMethodPrivateKey }, Group = "Connection", Help = "Password uses the 'password' secret slot; PrivateKey uses the 'privateKey' slot (with optional passphrase)." });
            schema.Add(new SettingDescriptor(SshSettingKeys.HostKeyFingerprint, SettingValueKindEnum.String, "Host key fingerprint") { Pattern = "^(SHA256:)?[A-Za-z0-9+/]{43}=?$", Group = "Connection", Help = "Expected SHA256 host key fingerprint as printed by ssh-keygen -lf (SHA256:...). When empty, the first successful connection pins the key it sees (trust on first use). A different key later is refused." });
            schema.Add(new SettingDescriptor(SshSettingKeys.ConnectTimeoutSeconds, SettingValueKindEnum.Integer, "Connect timeout (seconds)") { DefaultValue = "10", Minimum = 1, Maximum = 120, Group = "Connection", Help = "How long to wait for TCP connect, key exchange, and authentication. Default 10, minimum 1, maximum 120." });
            schema.Add(new SettingDescriptor(SshSettingKeys.KeepAliveSeconds, SettingValueKindEnum.Integer, "Keepalive interval (seconds)") { DefaultValue = "30", Minimum = 0, Maximum = 3600, Group = "Connection", Help = "Interval for SSH keepalives on pooled sessions. 0 disables them. Default 30." });
            schema.Add(new SettingDescriptor(SshSettingKeys.JumpHost, SettingValueKindEnum.String, "Jump host") { Group = "Jump host", Help = "Optional bastion to tunnel through (like ssh -J). The target host and port are then resolved from the bastion. Bind the 'jumpPrivateKey' or 'jumpPassword' slot." });
            schema.Add(new SettingDescriptor(SshSettingKeys.JumpPort, SettingValueKindEnum.Integer, "Jump host port") { DefaultValue = "22", Minimum = 1, Maximum = 65535, Group = "Jump host", Help = "Bastion SSH port. Default 22." });
            schema.Add(new SettingDescriptor(SshSettingKeys.JumpUsername, SettingValueKindEnum.String, "Jump host username") { Group = "Jump host", Help = "Bastion login user. Defaults to the target username." });
            schema.Add(new SettingDescriptor(SshSettingKeys.JumpHostKeyFingerprint, SettingValueKindEnum.String, "Jump host key fingerprint") { Pattern = "^(SHA256:)?[A-Za-z0-9+/]{43}=?$", Group = "Jump host", Help = "Expected SHA256 fingerprint of the bastion. Pinned on first use when empty." });
            schema.Add(new SettingDescriptor(SshSettingKeys.CommandTimeoutSeconds, SettingValueKindEnum.Integer, "Command timeout (seconds)") { DefaultValue = "60", Minimum = 1, Maximum = 900, Group = "Limits", Help = "Default and maximum run time of a command. Default 60, maximum 900. Calls may ask for less, never more." });
            schema.Add(new SettingDescriptor(SshSettingKeys.MaxOutputBytes, SettingValueKindEnum.Integer, "Max output bytes") { DefaultValue = "65536", Minimum = 1024, Maximum = 16 * 1024 * 1024, Group = "Limits", Help = "Bytes of stdout and of stderr kept per command (the end is kept). The call's output limit may be smaller. Default 65536." });
            schema.Add(new SettingDescriptor(SshSettingKeys.MaxFileReadBytes, SettingValueKindEnum.Integer, "Max file read bytes") { DefaultValue = "1048576", Minimum = 1024, Maximum = 64 * 1024 * 1024, Group = "Limits", Help = "Most bytes ssh_read_file reads in one call. Default 1 MiB." });
            schema.Add(new SettingDescriptor(SshSettingKeys.MaxFileWriteBytes, SettingValueKindEnum.Integer, "Max file write bytes") { DefaultValue = "1048576", Minimum = 1, Maximum = 64 * 1024 * 1024, Group = "Limits", Help = "Largest file ssh_write_file writes. Default 1 MiB." });
            schema.Add(new SettingDescriptor(SshSettingKeys.WorkingDirectory, SettingValueKindEnum.String, "Working directory") { Group = "Execution", Help = "Directory commands start in and relative paths resolve against. Empty means the login directory." });
            schema.Add(new SettingDescriptor(SshSettingKeys.Shell, SettingValueKindEnum.String, "Shell") { DefaultValue = "/bin/bash -lc", Group = "Execution", Help = "Program and options that run ssh_exec commands; the command is passed as one quoted argument. Default /bin/bash -lc. Empty runs commands through the login shell directly." });
            schema.Add(new SettingDescriptor(SshSettingKeys.Environment, SettingValueKindEnum.StringList, "Environment") { Group = "Execution", Help = "Variables exported before each ssh_exec command, one NAME=value per line." });
            schema.Add(new SettingDescriptor(SshSettingKeys.AllowSudo, SettingValueKindEnum.Boolean, "Allow sudo") { DefaultValue = "false", Group = "Safety", Help = "When false, any command using sudo, doas, or su is rejected. When true, the command after sudo is classified normally. Commands must use sudo -n because there is no terminal." });
            schema.Add(new SettingDescriptor(SshSettingKeys.ReadOnlyCommandPatterns, SettingValueKindEnum.StringList, "Read-only command patterns") { Group = "Safety", Help = "Extra regular expressions (one per line, case-insensitive, matched against the whole simple command) that classify a command as Read, for example ^myapp status( .*)?$. They never override a Destructive classification." });
            schema.Add(new SettingDescriptor(SshSettingKeys.DestructiveCommandPatterns, SettingValueKindEnum.StringList, "Destructive command patterns") { Group = "Safety", Help = "Extra regular expressions (one per line, case-insensitive, searched in each simple command) that classify a command as Destructive." });
            schema.Add(new SettingDescriptor(SshSettingKeys.DeniedCommandPatterns, SettingValueKindEnum.StringList, "Denied command patterns") { Group = "Safety", Help = "Regular expressions (one per line, case-insensitive, searched in the whole command and in each simple command) that reject a command outright, whatever the access mode." });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = "ssh";
            d.Family = "ssh";
            d.DisplayName = "SSH";
            d.Description = "Linux and Unix hosts over SSH: run classified shell commands, read and write files over SFTP, inspect system health, and manage services. Supports password and key authentication, a bastion jump host, and host key pinning.";
            d.Category = ConnectorCategoryEnum.Compute;
            d.IconKey = "terminal";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(SshSettingKeys.PasswordSlot, "Password", SecretKindEnum.Password) { Help = "Login password (field 'password'). Required when authMethod is Password." },
                new SecretSlotDescriptor(SshSettingKeys.PrivateKeySlot, "Private key", SecretKindEnum.PrivateKey) { Help = "OpenSSH or PEM private key (field 'privateKey') with an optional 'passphrase'. Required when authMethod is PrivateKey." },
                new SecretSlotDescriptor(SshSettingKeys.JumpPrivateKeySlot, "Jump host private key", SecretKindEnum.PrivateKey) { Help = "Private key for the jump host. Used when jumpHost is set; takes precedence over jumpPassword." },
                new SecretSlotDescriptor(SshSettingKeys.JumpPasswordSlot, "Jump host password", SecretKindEnum.Password) { Help = "Password for the jump host. Used when jumpHost is set and no jump host key is bound." }
            };
            d.DefaultInstructionsKey = "connector.ssh.instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(SshConnector).Assembly, "connector.ssh.instructions.md");
            d.DocumentationUrl = "https://man.openbsd.org/ssh";
            return d;
        }

        private static void ValidateFingerprint(ConnectorValidationResult result, ResourceSettings s, string key)
        {
            string? value = s.GetString(key);
            if (!String.IsNullOrWhiteSpace(value) && !SshFingerprint.IsValid(value))
                result.Add(key, "invalid_format", key + " must be a SHA256 fingerprint such as SHA256:" + new string('A', 43) + " (as printed by ssh-keygen -lf).");
        }

        private static void ValidatePatterns(ConnectorValidationResult result, ResourceSettings s, string key)
        {
            foreach (string pattern in s.GetStringList(key))
            {
                try
                {
                    Regex probe = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                    probe.IsMatch(String.Empty);
                }
                catch (ArgumentException e)
                {
                    result.Add(key, "invalid_format", "Pattern '" + Shorten(pattern, 80) + "' is not a valid regular expression: " + e.Message);
                }
                catch (RegexMatchTimeoutException)
                {
                    result.Add(key, "invalid_format", "Pattern '" + Shorten(pattern, 80) + "' is too expensive to evaluate.");
                }
            }
        }

        private static string Shorten(string value, int max)
        {
            string v = value.Replace('\n', ' ').Replace('\r', ' ');
            return v.Length > max ? v.Substring(0, max - 3) + "..." : v;
        }

        #endregion
    }
}
