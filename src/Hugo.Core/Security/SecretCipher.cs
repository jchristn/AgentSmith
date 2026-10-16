namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Authenticated encryption for stored secrets and credential secret keys (plan section 12.1). AES-256-GCM with a
    /// 12-byte random nonce and a 16-byte tag. The data key for each purpose is derived from the master key with
    /// HKDF-SHA256 (info is the purpose string, for example hugo-secrets-v1). Associated data binds each ciphertext to
    /// its row, so a ciphertext copied elsewhere fails to decrypt. Frame, base64 encoded:
    /// [1 byte format version][1 byte key version][12 byte nonce][16 byte tag][ciphertext].
    /// Holds the current master key and optionally a previous one, so values written under the previous key stay
    /// readable until rotation re-encrypts them. Thread-safe.
    /// </summary>
    public class SecretCipher
    {
        #region Public-Members

        /// <summary>
        /// Frame format version written by this build.
        /// </summary>
        public static readonly byte FormatVersion = 1;

        /// <summary>
        /// Minimum master key length in bytes.
        /// </summary>
        public static readonly int MinimumKeyBytes = 32;

        /// <summary>
        /// Key version new ciphertexts are written under. Minimum 1, maximum 255.
        /// </summary>
        public int KeyVersion
        {
            get
            {
                return _CurrentVersion;
            }
        }

        /// <summary>
        /// Every key version this cipher can decrypt, ascending.
        /// </summary>
        public IReadOnlyList<int> KeyVersions
        {
            get
            {
                return _Keys.Keys.OrderBy(k => k).ToList();
            }
        }

        #endregion

        #region Private-Members

        private static readonly int _HeaderBytes = 2 + 12 + 16;
        private readonly Dictionary<int, byte[]> _Keys = new Dictionary<int, byte[]>();
        private readonly int _CurrentVersion;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a current master key and an optional previous master key.
        /// </summary>
        /// <param name="currentKey">Current master key, at least 32 bytes.</param>
        /// <param name="currentVersion">Current key version. Minimum 1, maximum 255.</param>
        /// <param name="previousKey">Previous master key, or null.</param>
        /// <param name="previousVersion">Previous key version (ignored when previousKey is null). Minimum 1, maximum 255, different from currentVersion.</param>
        /// <exception cref="ArgumentNullException">Thrown when currentKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a key is shorter than 32 bytes or the versions collide.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a version is outside 1 to 255.</exception>
        public SecretCipher(byte[] currentKey, int currentVersion, byte[]? previousKey = null, int previousVersion = 0)
        {
            if (currentKey == null) throw new ArgumentNullException(nameof(currentKey));
            if (currentKey.Length < MinimumKeyBytes) throw new ArgumentException("The master key must be at least " + MinimumKeyBytes + " bytes.", nameof(currentKey));
            if (currentVersion < 1 || currentVersion > 255) throw new ArgumentOutOfRangeException(nameof(currentVersion), "Key versions must be between 1 and 255.");
            _CurrentVersion = currentVersion;
            _Keys[currentVersion] = (byte[])currentKey.Clone();
            SecretScrubber.Shared.AddKnownValue(Convert.ToBase64String(currentKey));
            if (previousKey != null)
            {
                if (previousKey.Length < MinimumKeyBytes) throw new ArgumentException("The previous master key must be at least " + MinimumKeyBytes + " bytes.", nameof(previousKey));
                if (previousVersion < 1 || previousVersion > 255) throw new ArgumentOutOfRangeException(nameof(previousVersion), "Key versions must be between 1 and 255.");
                if (previousVersion == currentVersion) throw new ArgumentException("The previous key version must differ from the current key version.", nameof(previousVersion));
                _Keys[previousVersion] = (byte[])previousKey.Clone();
                SecretScrubber.Shared.AddKnownValue(Convert.ToBase64String(previousKey));
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the associated data for a secret field: tenantId|secretId|fieldName.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="secretId">Secret identifier.</param>
        /// <param name="fieldName">Field name.</param>
        /// <returns>Associated data.</returns>
        public static string SecretFieldAssociatedData(string tenantId, string secretId, string fieldName)
        {
            return (tenantId ?? String.Empty) + "|" + (secretId ?? String.Empty) + "|" + (fieldName ?? String.Empty);
        }

        /// <summary>
        /// Read the key version byte of a ciphertext frame without decrypting it.
        /// </summary>
        /// <param name="encoded">Base64 frame.</param>
        /// <returns>Key version, or 0 when the frame is malformed.</returns>
        public static int ReadKeyVersion(string? encoded)
        {
            if (String.IsNullOrEmpty(encoded)) return 0;
            try
            {
                byte[] raw = Convert.FromBase64String(encoded);
                if (raw.Length < _HeaderBytes || raw[0] != FormatVersion) return 0;
                return raw[1];
            }
            catch (FormatException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Encrypt a value for a purpose under the current key version.
        /// </summary>
        /// <param name="purpose">Purpose (HKDF info), for example <see cref="Constants.KeyPurposeSecrets"/>.</param>
        /// <param name="plaintext">Plaintext.</param>
        /// <param name="associatedData">Associated data the ciphertext is bound to.</param>
        /// <returns>Base64 frame.</returns>
        /// <exception cref="ArgumentNullException">Thrown when purpose or plaintext is null.</exception>
        public string Encrypt(string purpose, string plaintext, string associatedData)
        {
            if (String.IsNullOrEmpty(purpose)) throw new ArgumentNullException(nameof(purpose));
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            if (purpose == Constants.KeyPurposeSecrets || purpose == Constants.KeyPurposeCredentials) SecretScrubber.Shared.AddKnownValue(plaintext);
            byte[] key = DeriveKey(_Keys[_CurrentVersion], purpose);
            byte[] data = Encoding.UTF8.GetBytes(plaintext);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] tag = new byte[16];
            byte[] cipher = new byte[data.Length];
            using (AesGcm gcm = new AesGcm(key, 16))
            {
                gcm.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(associatedData ?? String.Empty));
            }
            byte[] output = new byte[_HeaderBytes + cipher.Length];
            output[0] = FormatVersion;
            output[1] = (byte)_CurrentVersion;
            Buffer.BlockCopy(nonce, 0, output, 2, 12);
            Buffer.BlockCopy(tag, 0, output, 14, 16);
            Buffer.BlockCopy(cipher, 0, output, _HeaderBytes, cipher.Length);
            CryptographicOperations.ZeroMemory(key);
            return Convert.ToBase64String(output);
        }

        /// <summary>
        /// Decrypt a frame written for a purpose, under whichever configured key version the frame names.
        /// </summary>
        /// <param name="purpose">Purpose (HKDF info).</param>
        /// <param name="encoded">Base64 frame.</param>
        /// <param name="associatedData">Associated data the ciphertext was bound to.</param>
        /// <returns>Plaintext.</returns>
        /// <exception cref="ArgumentNullException">Thrown when purpose or encoded is null or empty.</exception>
        /// <exception cref="CryptographicException">Thrown when the frame is malformed, its key version is not configured, or authentication fails (wrong key, wrong associated data, or tampering).</exception>
        /// <exception cref="FormatException">Thrown when the value is not base64.</exception>
        public string Decrypt(string purpose, string encoded, string associatedData)
        {
            if (String.IsNullOrEmpty(purpose)) throw new ArgumentNullException(nameof(purpose));
            if (String.IsNullOrEmpty(encoded)) throw new ArgumentNullException(nameof(encoded));
            try
            {
                string plaintext = DecryptCore(purpose, encoded, associatedData);
                if (purpose == Constants.KeyPurposeSecrets || purpose == Constants.KeyPurposeCredentials) SecretScrubber.Shared.AddKnownValue(plaintext);
                HugoMetrics.Count(HugoMetricCatalog.SecretOperations, "operation", "decrypt", "outcome", "ok");
                return plaintext;
            }
            catch (Exception e) when (e is CryptographicException || e is FormatException)
            {
                HugoMetrics.Count(HugoMetricCatalog.SecretOperations, "operation", "decrypt", "outcome", "error");
                throw;
            }
        }

        /// <summary>
        /// Decrypt a frame and encrypt it again under the current key version (rotation).
        /// </summary>
        /// <param name="purpose">Purpose (HKDF info).</param>
        /// <param name="encoded">Base64 frame.</param>
        /// <param name="associatedData">Associated data.</param>
        /// <returns>New base64 frame.</returns>
        /// <exception cref="CryptographicException">Thrown when the frame cannot be decrypted.</exception>
        public string Reencrypt(string purpose, string encoded, string associatedData)
        {
            string plaintext = Decrypt(purpose, encoded, associatedData);
            return Encrypt(purpose, plaintext, associatedData);
        }

        /// <summary>
        /// Encrypt a credential secret key (purpose hugo-credentials-v1, associated data the credential identifier).
        /// </summary>
        /// <param name="plaintext">Secret key.</param>
        /// <param name="credentialId">Credential identifier.</param>
        /// <returns>Base64 frame.</returns>
        public string EncryptCredential(string plaintext, string credentialId)
        {
            return Encrypt(Constants.KeyPurposeCredentials, plaintext, credentialId);
        }

        /// <summary>
        /// Decrypt a credential secret key.
        /// </summary>
        /// <param name="encoded">Base64 frame.</param>
        /// <param name="credentialId">Credential identifier.</param>
        /// <returns>Secret key.</returns>
        /// <exception cref="CryptographicException">Thrown when decryption fails.</exception>
        public string DecryptCredential(string encoded, string credentialId)
        {
            return Decrypt(Constants.KeyPurposeCredentials, encoded, credentialId);
        }

        /// <summary>
        /// Whether a key version is configured.
        /// </summary>
        /// <param name="keyVersion">Key version.</param>
        /// <returns>True when this cipher holds the key.</returns>
        public bool HasKeyVersion(int keyVersion)
        {
            return _Keys.ContainsKey(keyVersion);
        }

        #endregion

        #region Private-Methods

        private string DecryptCore(string purpose, string encoded, string associatedData)
        {
            byte[] raw = Convert.FromBase64String(encoded);
            if (raw.Length < _HeaderBytes) throw new CryptographicException("Encrypted value is truncated.");
            if (raw[0] != FormatVersion) throw new CryptographicException("Unsupported ciphertext format version " + raw[0] + ".");
            int version = raw[1];
            if (!_Keys.TryGetValue(version, out byte[]? master)) throw new CryptographicException("No master key is configured for key version " + version + ".");
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] cipher = new byte[raw.Length - _HeaderBytes];
            Buffer.BlockCopy(raw, 2, nonce, 0, 12);
            Buffer.BlockCopy(raw, 14, tag, 0, 16);
            Buffer.BlockCopy(raw, _HeaderBytes, cipher, 0, cipher.Length);
            byte[] data = new byte[cipher.Length];
            byte[] key = DeriveKey(master, purpose);
            try
            {
                using (AesGcm gcm = new AesGcm(key, 16))
                {
                    gcm.Decrypt(nonce, cipher, tag, data, Encoding.UTF8.GetBytes(associatedData ?? String.Empty));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
            return Encoding.UTF8.GetString(data);
        }

        private static byte[] DeriveKey(byte[] master, string purpose)
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes(purpose));
        }

        #endregion
    }
}
