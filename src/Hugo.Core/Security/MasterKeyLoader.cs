namespace Hugo.Core.Security
{
    using System;
    using System.IO;
    using System.Security.Cryptography;

    /// <summary>
    /// Loads the secrets master key (P2.1): HUGO_MASTER_KEY first, then the development-only settings key, then the
    /// development-only key file (created with a random key when missing). Every non-environment source reports a loud
    /// warning. Thread-safe (stateless).
    /// </summary>
    public static class MasterKeyLoader
    {
        #region Public-Methods

        /// <summary>
        /// Load the master key configuration.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="warn">Warning sink.</param>
        /// <returns>Loaded configuration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        /// <exception cref="MasterKeyException">Thrown when no key is configured or a key is malformed.</exception>
        public static MasterKeyLoadResult Load(MasterKeyOptions options, Action<string>? warn)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            byte[] current;
            MasterKeySourceEnum source;
            if (!String.IsNullOrWhiteSpace(options.EnvironmentKey))
            {
                current = Decode(options.EnvironmentKey!, "HUGO_MASTER_KEY");
                source = MasterKeySourceEnum.Environment;
            }
            else if (!String.IsNullOrWhiteSpace(options.SettingsKey))
            {
                current = Decode(options.SettingsKey!, "Secrets.MasterKey");
                source = MasterKeySourceEnum.SettingsFile;
                warn?.Invoke("WARNING: the secrets master key comes from the settings file (Secrets.MasterKey). This is for development only. Set HUGO_MASTER_KEY in production.");
            }
            else if (!String.IsNullOrWhiteSpace(options.KeyFile))
            {
                string path = options.KeyFile!;
                if (File.Exists(path))
                {
                    current = Decode(File.ReadAllText(path).Trim(), "key file " + path);
                    source = MasterKeySourceEnum.KeyFile;
                }
                else
                {
                    string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    current = RandomNumberGenerator.GetBytes(32);
                    File.WriteAllText(path, Convert.ToBase64String(current));
                    source = MasterKeySourceEnum.GeneratedKeyFile;
                }
                warn?.Invoke("WARNING: the secrets master key comes from the development key file " + Path.GetFullPath(path) + (source == MasterKeySourceEnum.GeneratedKeyFile ? " (generated now)" : String.Empty) + ". Anyone who can read that file can decrypt every secret. Set HUGO_MASTER_KEY in production.");
            }
            else
            {
                throw new MasterKeyException("No secrets master key is configured. Set HUGO_MASTER_KEY to a base64 value of at least 32 bytes (for example: openssl rand -base64 32).");
            }

            byte[]? previous = null;
            int previousVersion = options.PreviousKeyVersion > 0 ? options.PreviousKeyVersion : options.KeyVersion - 1;
            if (!String.IsNullOrWhiteSpace(options.EnvironmentPreviousKey))
            {
                if (previousVersion < 1) throw new MasterKeyException("HUGO_MASTER_KEY_PREVIOUS is set but there is no previous key version: set HUGO_MASTER_KEY_VERSION to 2 or higher, or set HUGO_MASTER_KEY_PREVIOUS_VERSION.");
                if (previousVersion == options.KeyVersion) throw new MasterKeyException("The previous master key version must differ from the current version " + options.KeyVersion + ".");
                previous = Decode(options.EnvironmentPreviousKey!, "HUGO_MASTER_KEY_PREVIOUS");
            }

            SecretCipher cipher = new SecretCipher(current, options.KeyVersion, previous, previous != null ? previousVersion : 0);
            CryptographicOperations.ZeroMemory(current);
            if (previous != null) CryptographicOperations.ZeroMemory(previous);
            return new MasterKeyLoadResult(cipher, source, previous != null);
        }

        /// <summary>
        /// Decode a base64 master key and check its length.
        /// </summary>
        /// <param name="value">Base64 value.</param>
        /// <param name="what">Name of the source, for messages.</param>
        /// <returns>Key bytes.</returns>
        /// <exception cref="MasterKeyException">Thrown when the value is not base64 or shorter than 32 bytes.</exception>
        public static byte[] Decode(string value, string what)
        {
            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(value.Trim());
            }
            catch (FormatException e)
            {
                throw new MasterKeyException("The master key in " + what + " is not valid base64.", e);
            }
            if (decoded.Length < SecretCipher.MinimumKeyBytes) throw new MasterKeyException("The master key in " + what + " decodes to " + decoded.Length + " bytes; at least " + SecretCipher.MinimumKeyBytes + " are required.");
            return decoded;
        }

        #endregion
    }
}
