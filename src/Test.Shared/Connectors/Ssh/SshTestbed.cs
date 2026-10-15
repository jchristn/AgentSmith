namespace Test.Shared.Connectors.Ssh
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Locations and credentials of the SSH testbed (<c>docker/testbed/ssh/compose.yaml</c>). Values can be overridden
    /// with <c>HUGO_TESTBED_SSH_HOST</c>, <c>HUGO_TESTBED_SSH_PORT</c>, <c>HUGO_TESTBED_SSH_BASTION_PORT</c>, and
    /// <c>HUGO_TESTBED_SSH_KEYS</c>.
    /// </summary>
    public static class SshTestbed
    {
        #region Public-Members

        /// <summary>
        /// Whether testbed suites run (<c>HUGO_TESTBED=1</c>).
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return Environment.GetEnvironmentVariable("HUGO_TESTBED") == "1";
            }
        }

        /// <summary>
        /// Skip reason when the testbed is disabled.
        /// </summary>
        public static string SkipReason
        {
            get
            {
                return "SSH testbed not enabled. Run docker/testbed/ssh/generate-keys.sh, start docker/testbed/ssh/compose.yaml, and set HUGO_TESTBED=1.";
            }
        }

        /// <summary>
        /// Host of both containers from the test machine.
        /// </summary>
        public static string Host
        {
            get
            {
                return Environment.GetEnvironmentVariable("HUGO_TESTBED_SSH_HOST") ?? "127.0.0.1";
            }
        }

        /// <summary>
        /// Target port.
        /// </summary>
        public static int TargetPort
        {
            get
            {
                return EnvInt("HUGO_TESTBED_SSH_PORT", 28022);
            }
        }

        /// <summary>
        /// Bastion port.
        /// </summary>
        public static int BastionPort
        {
            get
            {
                return EnvInt("HUGO_TESTBED_SSH_BASTION_PORT", 28023);
            }
        }

        /// <summary>
        /// Target host name as seen from the bastion.
        /// </summary>
        public static string TargetFromBastion { get; } = "ssh-target";

        /// <summary>
        /// Target sshd port as seen from the bastion.
        /// </summary>
        public static int TargetPortFromBastion { get; } = 2222;

        /// <summary>
        /// Password user.
        /// </summary>
        public static string PasswordUser { get; } = "hugo";

        /// <summary>
        /// Password of <see cref="PasswordUser"/>.
        /// </summary>
        public static string Password { get; } = "hugo-testbed-password";

        /// <summary>
        /// Key-only user.
        /// </summary>
        public static string KeyUser { get; } = "hugokey";

        /// <summary>
        /// Passphrase of <c>id_ed25519_passphrase</c>.
        /// </summary>
        public static string KeyPassphrase { get; } = "hugo-testbed-passphrase";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Locate the generated key directory.
        /// </summary>
        /// <returns>Directory path.</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the keys have not been generated.</exception>
        public static string KeysDirectory()
        {
            string? env = Environment.GetEnvironmentVariable("HUGO_TESTBED_SSH_KEYS");
            if (!String.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "docker", "testbed", "generated", "ssh");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("docker/testbed/generated/ssh was not found. Run docker/testbed/ssh/generate-keys.sh.");
        }

        /// <summary>
        /// Read a generated private key.
        /// </summary>
        /// <param name="name">File name, for example <c>id_ed25519</c>.</param>
        /// <returns>Key text.</returns>
        public static string ReadKey(string name)
        {
            return File.ReadAllText(Path.Combine(KeysDirectory(), name));
        }

        /// <summary>
        /// Settings for the target with password authentication.
        /// </summary>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> PasswordSettings()
        {
            return new Dictionary<string, string>
            {
                { "host", Host },
                { "port", TargetPort.ToString(CultureInfo.InvariantCulture) },
                { "username", PasswordUser },
                { "authMethod", "Password" },
                { "connectTimeoutSeconds", "10" },
                { "commandTimeoutSeconds", "60" }
            };
        }

        /// <summary>
        /// Settings for the target with key authentication as the key-only user.
        /// </summary>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> KeySettings()
        {
            Dictionary<string, string> s = PasswordSettings();
            s["username"] = KeyUser;
            s["authMethod"] = "PrivateKey";
            return s;
        }

        /// <summary>
        /// Secrets with the password slot bound.
        /// </summary>
        /// <param name="password">Password, or null for the testbed password.</param>
        /// <returns>Secrets.</returns>
        public static Dictionary<string, Dictionary<string, string>> PasswordSecrets(string? password = null)
        {
            return new Dictionary<string, Dictionary<string, string>>
            {
                { "password", new Dictionary<string, string> { { "password", password ?? Password } } }
            };
        }

        /// <summary>
        /// Secrets with the private key slot bound.
        /// </summary>
        /// <param name="keyName">Key file name.</param>
        /// <param name="passphrase">Passphrase, or null.</param>
        /// <returns>Secrets.</returns>
        public static Dictionary<string, Dictionary<string, string>> KeySecrets(string keyName = "id_ed25519", string? passphrase = null)
        {
            Dictionary<string, string> fields = new Dictionary<string, string> { { "privateKey", ReadKey(keyName) } };
            if (passphrase != null) fields["passphrase"] = passphrase;
            return new Dictionary<string, Dictionary<string, string>> { { "privateKey", fields } };
        }

        #endregion

        #region Private-Methods

        private static int EnvInt(string name, int fallback)
        {
            string? v = Environment.GetEnvironmentVariable(name);
            return v != null && Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        #endregion
    }
}
