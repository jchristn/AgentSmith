namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Ssh.Classification;

    /// <summary>
    /// Typed snapshot of an SSH resource's settings, with defaults and range clamping applied. Not thread-safe for
    /// mutation; build one per call with <see cref="FromSettings"/>.
    /// </summary>
    public class SshConnectionOptions
    {
        #region Public-Members

        /// <summary>
        /// Target host name or address. Required.
        /// </summary>
        public string Host { get; set; } = String.Empty;

        /// <summary>
        /// Target port. Minimum 1, maximum 65535, default 22.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Login user name. Required.
        /// </summary>
        public string Username { get; set; } = String.Empty;

        /// <summary>
        /// Authentication method, <c>Password</c> or <c>PrivateKey</c>. Default <c>PrivateKey</c>.
        /// </summary>
        public string AuthMethod { get; set; } = SshSettingKeys.AuthMethodPrivateKey;

        /// <summary>
        /// Pinned target host key fingerprint (normalized), or null when not yet pinned.
        /// </summary>
        public string? HostKeyFingerprint { get; set; } = null;

        /// <summary>
        /// Bastion host, or null when connecting directly.
        /// </summary>
        public string? JumpHost { get; set; } = null;

        /// <summary>
        /// Bastion port. Minimum 1, maximum 65535, default 22.
        /// </summary>
        public int JumpPort
        {
            get
            {
                return _JumpPort;
            }
            set
            {
                _JumpPort = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Bastion user name. Defaults to <see cref="Username"/> when empty.
        /// </summary>
        public string? JumpUsername { get; set; } = null;

        /// <summary>
        /// Pinned bastion host key fingerprint (normalized), or null when not yet pinned.
        /// </summary>
        public string? JumpHostKeyFingerprint { get; set; } = null;

        /// <summary>
        /// Connect timeout. Minimum 1 second, maximum 120 seconds, default 10 seconds.
        /// </summary>
        public int ConnectTimeoutSeconds
        {
            get
            {
                return _ConnectTimeoutSeconds;
            }
            set
            {
                _ConnectTimeoutSeconds = Math.Clamp(value, 1, 120);
            }
        }

        /// <summary>
        /// Default and maximum command timeout. Minimum 1 second, maximum 900 seconds, default 60 seconds.
        /// </summary>
        public int CommandTimeoutSeconds
        {
            get
            {
                return _CommandTimeoutSeconds;
            }
            set
            {
                _CommandTimeoutSeconds = Math.Clamp(value, 1, 900);
            }
        }

        /// <summary>
        /// Keepalive interval for pooled sessions. 0 disables keepalives. Minimum 0, maximum 3600, default 30 seconds.
        /// </summary>
        public int KeepAliveSeconds
        {
            get
            {
                return _KeepAliveSeconds;
            }
            set
            {
                _KeepAliveSeconds = Math.Clamp(value, 0, 3600);
            }
        }

        /// <summary>
        /// Maximum bytes of output kept per stream (stdout, stderr) before the call-level limits apply. Minimum 1024,
        /// maximum 16 MiB, default 65536.
        /// </summary>
        public int MaxOutputBytes
        {
            get
            {
                return _MaxOutputBytes;
            }
            set
            {
                _MaxOutputBytes = Math.Clamp(value, 1024, 16 * 1024 * 1024);
            }
        }

        /// <summary>
        /// Working directory for commands and relative paths, or null for the login directory.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Whether sudo, doas, and su are allowed. Default false.
        /// </summary>
        public bool AllowSudo { get; set; } = false;

        /// <summary>
        /// Shell wrapper for <c>ssh_exec</c>, for example <c>/bin/bash -lc</c>. The command is passed as one quoted
        /// argument. Empty runs the command through the login shell directly. Default <c>/bin/bash -lc</c>.
        /// </summary>
        public string Shell { get; set; } = "/bin/bash -lc";

        /// <summary>
        /// Extra read-only command patterns. Never null.
        /// </summary>
        public List<string> ReadOnlyCommandPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Extra destructive command patterns. Never null.
        /// </summary>
        public List<string> DestructiveCommandPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Patterns that reject a command. Never null.
        /// </summary>
        public List<string> DeniedCommandPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Environment entries (<c>KEY=VALUE</c>) exported before each <c>ssh_exec</c> command. Never null.
        /// </summary>
        public List<string> Environment { get; set; } = new List<string>();

        /// <summary>
        /// Maximum bytes read from one file. Minimum 1024, maximum 64 MiB, default 1 MiB.
        /// </summary>
        public int MaxFileReadBytes
        {
            get
            {
                return _MaxFileReadBytes;
            }
            set
            {
                _MaxFileReadBytes = Math.Clamp(value, 1024, 64 * 1024 * 1024);
            }
        }

        /// <summary>
        /// Maximum bytes written to one file. Minimum 1, maximum 64 MiB, default 1 MiB.
        /// </summary>
        public int MaxFileWriteBytes
        {
            get
            {
                return _MaxFileWriteBytes;
            }
            set
            {
                _MaxFileWriteBytes = Math.Clamp(value, 1, 64 * 1024 * 1024);
            }
        }

        /// <summary>
        /// Whether a bastion is configured.
        /// </summary>
        public bool UsesJumpHost
        {
            get
            {
                return !String.IsNullOrWhiteSpace(JumpHost);
            }
        }

        #endregion

        #region Private-Members

        private int _Port = 22;
        private int _JumpPort = 22;
        private int _ConnectTimeoutSeconds = 10;
        private int _CommandTimeoutSeconds = 60;
        private int _KeepAliveSeconds = 30;
        private int _MaxOutputBytes = 65536;
        private int _MaxFileReadBytes = 1024 * 1024;
        private int _MaxFileWriteBytes = 1024 * 1024;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build options from resource settings, applying defaults and clamping.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static SshConnectionOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            SshConnectionOptions o = new SshConnectionOptions();
            o.Host = (settings.GetString(SshSettingKeys.Host) ?? String.Empty).Trim();
            o.Port = settings.GetInt(SshSettingKeys.Port, 22, 1, 65535);
            o.Username = (settings.GetString(SshSettingKeys.Username) ?? String.Empty).Trim();
            o.AuthMethod = settings.GetString(SshSettingKeys.AuthMethod, SshSettingKeys.AuthMethodPrivateKey) ?? SshSettingKeys.AuthMethodPrivateKey;
            o.HostKeyFingerprint = SshFingerprint.Normalize(settings.GetString(SshSettingKeys.HostKeyFingerprint));
            o.JumpHost = settings.GetString(SshSettingKeys.JumpHost)?.Trim();
            o.JumpPort = settings.GetInt(SshSettingKeys.JumpPort, 22, 1, 65535);
            o.JumpUsername = settings.GetString(SshSettingKeys.JumpUsername)?.Trim();
            o.JumpHostKeyFingerprint = SshFingerprint.Normalize(settings.GetString(SshSettingKeys.JumpHostKeyFingerprint));
            o.ConnectTimeoutSeconds = settings.GetInt(SshSettingKeys.ConnectTimeoutSeconds, 10, 1, 120);
            o.CommandTimeoutSeconds = settings.GetInt(SshSettingKeys.CommandTimeoutSeconds, 60, 1, 900);
            o.KeepAliveSeconds = settings.GetInt(SshSettingKeys.KeepAliveSeconds, 30, 0, 3600);
            o.MaxOutputBytes = settings.GetInt(SshSettingKeys.MaxOutputBytes, 65536, 1024, 16 * 1024 * 1024);
            o.WorkingDirectory = settings.GetString(SshSettingKeys.WorkingDirectory)?.Trim();
            o.AllowSudo = settings.GetBool(SshSettingKeys.AllowSudo, false);
            o.Shell = settings.Has(SshSettingKeys.Shell) ? (settings.GetString(SshSettingKeys.Shell) ?? String.Empty).Trim() : "/bin/bash -lc";
            o.ReadOnlyCommandPatterns = settings.GetStringList(SshSettingKeys.ReadOnlyCommandPatterns);
            o.DestructiveCommandPatterns = settings.GetStringList(SshSettingKeys.DestructiveCommandPatterns);
            o.DeniedCommandPatterns = settings.GetStringList(SshSettingKeys.DeniedCommandPatterns);
            o.Environment = settings.GetStringList(SshSettingKeys.Environment);
            o.MaxFileReadBytes = settings.GetInt(SshSettingKeys.MaxFileReadBytes, 1024 * 1024, 1024, 64 * 1024 * 1024);
            o.MaxFileWriteBytes = settings.GetInt(SshSettingKeys.MaxFileWriteBytes, 1024 * 1024, 1, 64 * 1024 * 1024);
            if (String.IsNullOrEmpty(o.WorkingDirectory)) o.WorkingDirectory = null;
            if (String.IsNullOrEmpty(o.JumpHost)) o.JumpHost = null;
            return o;
        }

        /// <summary>
        /// Build classifier options from these settings.
        /// </summary>
        /// <returns>Classifier options.</returns>
        public SshClassifierOptions ToClassifierOptions()
        {
            SshClassifierOptions c = new SshClassifierOptions();
            c.AllowSudo = AllowSudo;
            c.DeniedPatterns = new List<string>(DeniedCommandPatterns);
            c.DestructivePatterns = new List<string>(DestructiveCommandPatterns);
            c.ReadOnlyPatterns = new List<string>(ReadOnlyCommandPatterns);
            return c;
        }

        /// <summary>
        /// Resolve a path against <see cref="WorkingDirectory"/>. Absolute paths and paths starting with <c>~</c> are
        /// returned unchanged.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Resolved path.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the path is empty or contains NUL or newline characters.</exception>
        public string ResolvePath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ConnectorException("invalid_argument", "A path is required.");
            if (path.IndexOf('\0') >= 0 || path.IndexOf('\n') >= 0 || path.IndexOf('\r') >= 0)
                throw new ConnectorException("invalid_argument", "The path contains a NUL or newline character.");
            if (path.StartsWith('/') || path.StartsWith('~') || String.IsNullOrEmpty(WorkingDirectory)) return path;
            return WorkingDirectory.TrimEnd('/') + "/" + path;
        }

        #endregion
    }
}
