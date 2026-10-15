namespace Hugo.Connectors.Ssh
{
    /// <summary>
    /// Setting keys and secret slot names of the <c>ssh</c> connector. These are part of the resource contract and are
    /// not configurable. Thread-safe.
    /// </summary>
    public static class SshSettingKeys
    {
        /// <summary>Host name or IP address of the target.</summary>
        public const string Host = "host";

        /// <summary>TCP port of the target.</summary>
        public const string Port = "port";

        /// <summary>Login user name.</summary>
        public const string Username = "username";

        /// <summary>Authentication method: <c>Password</c> or <c>PrivateKey</c>.</summary>
        public const string AuthMethod = "authMethod";

        /// <summary>Pinned SHA256 host key fingerprint of the target.</summary>
        public const string HostKeyFingerprint = "hostKeyFingerprint";

        /// <summary>Optional bastion (jump host) name or address.</summary>
        public const string JumpHost = "jumpHost";

        /// <summary>Bastion TCP port.</summary>
        public const string JumpPort = "jumpPort";

        /// <summary>Bastion login user name.</summary>
        public const string JumpUsername = "jumpUsername";

        /// <summary>Pinned SHA256 host key fingerprint of the bastion.</summary>
        public const string JumpHostKeyFingerprint = "jumpHostKeyFingerprint";

        /// <summary>Connect timeout in seconds.</summary>
        public const string ConnectTimeoutSeconds = "connectTimeoutSeconds";

        /// <summary>Default and maximum command timeout in seconds.</summary>
        public const string CommandTimeoutSeconds = "commandTimeoutSeconds";

        /// <summary>Keepalive interval in seconds.</summary>
        public const string KeepAliveSeconds = "keepAliveSeconds";

        /// <summary>Maximum bytes of command output kept per stream.</summary>
        public const string MaxOutputBytes = "maxOutputBytes";

        /// <summary>Working directory for commands and relative paths.</summary>
        public const string WorkingDirectory = "workingDirectory";

        /// <summary>Whether sudo, doas, and su are allowed.</summary>
        public const string AllowSudo = "allowSudo";

        /// <summary>Shell wrapper used to run <c>ssh_exec</c> commands.</summary>
        public const string Shell = "shell";

        /// <summary>Extra read-only command patterns.</summary>
        public const string ReadOnlyCommandPatterns = "readOnlyCommandPatterns";

        /// <summary>Extra destructive command patterns.</summary>
        public const string DestructiveCommandPatterns = "destructiveCommandPatterns";

        /// <summary>Command patterns that are always rejected.</summary>
        public const string DeniedCommandPatterns = "deniedCommandPatterns";

        /// <summary>Environment variables (<c>KEY=VALUE</c> per line) exported before each command.</summary>
        public const string Environment = "environment";

        /// <summary>Maximum bytes read from a file by <c>ssh_read_file</c>.</summary>
        public const string MaxFileReadBytes = "maxFileReadBytes";

        /// <summary>Maximum bytes written by <c>ssh_write_file</c>.</summary>
        public const string MaxFileWriteBytes = "maxFileWriteBytes";

        /// <summary>Secret slot holding a Password secret for the target.</summary>
        public const string PasswordSlot = "password";

        /// <summary>Secret slot holding a PrivateKey secret for the target.</summary>
        public const string PrivateKeySlot = "privateKey";

        /// <summary>Secret slot holding a Password secret for the bastion.</summary>
        public const string JumpPasswordSlot = "jumpPassword";

        /// <summary>Secret slot holding a PrivateKey secret for the bastion.</summary>
        public const string JumpPrivateKeySlot = "jumpPrivateKey";

        /// <summary>Field of a Password secret holding the password.</summary>
        public const string PasswordField = "password";

        /// <summary>Field of a PrivateKey secret holding the PEM or OpenSSH key text.</summary>
        public const string PrivateKeyField = "privateKey";

        /// <summary>Field of a PrivateKey secret holding the optional passphrase.</summary>
        public const string PassphraseField = "passphrase";

        /// <summary>Value of <see cref="AuthMethod"/> selecting password authentication.</summary>
        public const string AuthMethodPassword = "Password";

        /// <summary>Value of <see cref="AuthMethod"/> selecting private key authentication.</summary>
        public const string AuthMethodPrivateKey = "PrivateKey";
    }
}
