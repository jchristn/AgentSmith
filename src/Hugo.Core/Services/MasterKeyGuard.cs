namespace Hugo.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// Startup check of the secrets master key against the canary rows in master_key_versions (P2.1). The canary of the
    /// current key version must decrypt. A new key version is accepted only when a configured key decrypts an existing
    /// canary (the previous key during rotation) or nothing is encrypted yet; otherwise startup is refused. Thread-safe
    /// (stateless apart from the database).
    /// </summary>
    public static class MasterKeyGuard
    {
        #region Public-Members

        /// <summary>
        /// Known plaintext of every canary.
        /// </summary>
        public static readonly string CanaryPlaintext = "hugo-master-key-canary";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Associated data of the canary for a key version.
        /// </summary>
        /// <param name="keyVersion">Key version.</param>
        /// <returns>Associated data.</returns>
        public static string CanaryAssociatedData(int keyVersion)
        {
            return "master_key_versions|" + keyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Verify the configured key against the canaries, recording a canary for a new key version.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="cipher">Cipher with the configured keys.</param>
        /// <param name="log">Optional log sink.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="MasterKeyException">Thrown when the configured key cannot decrypt the stored canary and encrypted rows exist.</exception>
        public static async Task VerifyAsync(DatabaseDriverBase database, SecretCipher cipher, Action<string>? log, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));

            List<MasterKeyVersion> versions = await database.MasterKeys.ReadAllAsync(token).ConfigureAwait(false);
            MasterKeyVersion? current = versions.FirstOrDefault(v => v.KeyVersion == cipher.KeyVersion);
            if (current != null)
            {
                if (!Decrypts(cipher, current))
                {
                    throw new MasterKeyException("The configured master key (version " + cipher.KeyVersion + ") does not decrypt the stored canary for that version. Refusing to start: restore the correct HUGO_MASTER_KEY.");
                }
                if (!current.IsCurrent) await database.MasterKeys.SetCurrentAsync(current.KeyVersion, token).ConfigureAwait(false);
                await ReportPendingAsync(database, cipher, log, token).ConfigureAwait(false);
                return;
            }

            // A key version with no canary yet: either first boot, or the operator moved to a new key.
            bool anyDecrypts = versions.Any(v => cipher.HasKeyVersion(v.KeyVersion) && Decrypts(cipher, v));
            if (versions.Count > 0 && !anyDecrypts)
            {
                long encrypted = await CountEncryptedRowsAsync(database, token).ConfigureAwait(false);
                if (encrypted > 0)
                {
                    throw new MasterKeyException("The configured master key (version " + cipher.KeyVersion + ") is new, and no configured key decrypts an existing canary (versions " + String.Join(", ", versions.Select(v => v.KeyVersion)) + ") while " + encrypted + " encrypted rows exist. Refusing to start: to rotate, set HUGO_MASTER_KEY_PREVIOUS to the old key.");
                }
                log?.Invoke("no encrypted rows exist, so the canaries of key versions " + String.Join(", ", versions.Select(v => v.KeyVersion)) + " are superseded by version " + cipher.KeyVersion);
            }

            MasterKeyVersion created = new MasterKeyVersion();
            created.KeyVersion = cipher.KeyVersion;
            created.Canary = cipher.Encrypt(Constants.KeyPurposeSecrets, CanaryPlaintext, CanaryAssociatedData(cipher.KeyVersion));
            created.IsCurrent = true;
            await database.MasterKeys.UpsertAsync(created, token).ConfigureAwait(false);
            await database.MasterKeys.SetCurrentAsync(created.KeyVersion, token).ConfigureAwait(false);
            log?.Invoke("recorded the canary for master key version " + cipher.KeyVersion);
            await ReportPendingAsync(database, cipher, log, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static bool Decrypts(SecretCipher cipher, MasterKeyVersion version)
        {
            try
            {
                return cipher.Decrypt(Constants.KeyPurposeSecrets, version.Canary, CanaryAssociatedData(version.KeyVersion)) == CanaryPlaintext;
            }
            catch (Exception e) when (e is CryptographicException || e is FormatException || e is ArgumentException)
            {
                return false;
            }
        }

        private static async Task<long> CountEncryptedRowsAsync(DatabaseDriverBase database, CancellationToken token)
        {
            long values = await database.Secrets.CountValuesAsync(token).ConfigureAwait(false);
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = 1;
            EnumerationResult<Credential> credentials = await database.Credentials.EnumerateAsync(null, query, token).ConfigureAwait(false);
            return values + credentials.TotalRecords;
        }

        private static async Task ReportPendingAsync(DatabaseDriverBase database, SecretCipher cipher, Action<string>? log, CancellationToken token)
        {
            long pending = await database.Secrets.CountValuesNotAtVersionAsync(cipher.KeyVersion, token).ConfigureAwait(false);
            if (pending > 0) log?.Invoke(pending + " secret values are encrypted under an older master key version; run POST /v1.0/api/system/master-key/rotate to re-encrypt them");
        }

        #endregion
    }
}
