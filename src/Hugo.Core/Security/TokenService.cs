namespace Hugo.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Hugo.Core.Serialization;

    /// <summary>
    /// Issues and validates opaque session tokens: AES-256-CBC with a random IV per token, then HMAC-SHA256 over
    /// IV and ciphertext for legacy tokens. New tokens use versioned AES-256-GCM frames under the master-key
    /// HKDF session subkey. Thread-safe.
    /// </summary>
    public class TokenService
    {
        #region Public-Members

        /// <summary>
        /// Default token lifetime in minutes.
        /// </summary>
        public int LifetimeMinutes
        {
            get
            {
                return _LifetimeMinutes;
            }
        }

        #endregion

        #region Private-Members

        private readonly SecretCipher? _Cipher;
        private readonly TokenService? _Legacy;
        private readonly byte[] _EncryptionKey = Array.Empty<byte>();
        private readonly byte[] _MacKey = Array.Empty<byte>();
        private readonly string _Issuer;
        private readonly int _LifetimeMinutes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="signingKey">Server signing key (any length; derived with SHA-256).</param>
        /// <param name="issuer">Issuer.</param>
        /// <param name="lifetimeMinutes">Default lifetime. Minimum 1, maximum 10080.</param>
        /// <exception cref="ArgumentNullException">Thrown when signingKey or issuer is null or empty.</exception>
        public TokenService(string signingKey, string issuer, int lifetimeMinutes)
        {
            if (String.IsNullOrEmpty(signingKey)) throw new ArgumentNullException(nameof(signingKey));
            if (String.IsNullOrEmpty(issuer)) throw new ArgumentNullException(nameof(issuer));
            _EncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes("enc:" + signingKey));
            _MacKey = SHA256.HashData(Encoding.UTF8.GetBytes("mac:" + signingKey));
            _Issuer = issuer;
            _LifetimeMinutes = Math.Clamp(lifetimeMinutes, 1, 10080);
        }

        /// <summary>
        /// Instantiate a master-key-backed codec. New tokens use the current cipher key version;
        /// configured previous keys validate older frames. Legacy signing-key tokens remain valid until expiry or revocation.
        /// </summary>
        /// <param name="cipher">Shared versioned master-key cipher.</param>
        /// <param name="issuer">Issuer, bound as authenticated associated data.</param>
        /// <param name="lifetimeMinutes">Default lifetime, clamped to 1 through 10080 minutes.</param>
        /// <param name="legacySigningKey">Previous signing key for pre-upgrade tokens, or null to disable compatibility.</param>
        /// <exception cref="ArgumentNullException">Thrown when cipher or issuer is null or empty.</exception>
        public TokenService(SecretCipher cipher, string issuer, int lifetimeMinutes, string? legacySigningKey = null)
        {
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            if (String.IsNullOrEmpty(issuer)) throw new ArgumentNullException(nameof(issuer));
            _Issuer = issuer;
            _LifetimeMinutes = Math.Clamp(lifetimeMinutes, 1, 10080);
            if (!String.IsNullOrEmpty(legacySigningKey)) _Legacy = new TokenService(legacySigningKey, issuer, lifetimeMinutes);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Issue a token for a payload.
        /// </summary>
        /// <param name="payload">Payload.</param>
        /// <returns>Opaque token.</returns>
        /// <exception cref="ArgumentNullException">Thrown when payload is null.</exception>
        public string Issue(TokenPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            payload.Issuer = _Issuer;

            string serialized = JsonSerializer.Serialize(payload, Serializer.Options);
            if (_Cipher != null) return "hugo1." + _Cipher.Encrypt(Constants.KeyPurposeSessions, serialized, _Issuer);
            byte[] plaintext = Encoding.UTF8.GetBytes(serialized);
            using (Aes aes = Aes.Create())
            {
                aes.Key = _EncryptionKey;
                aes.GenerateIV();
                byte[] cipher = aes.EncryptCbc(plaintext, aes.IV);
                byte[] body = new byte[aes.IV.Length + cipher.Length];
                Buffer.BlockCopy(aes.IV, 0, body, 0, aes.IV.Length);
                Buffer.BlockCopy(cipher, 0, body, aes.IV.Length, cipher.Length);
                byte[] mac = HMACSHA256.HashData(_MacKey, body);
                byte[] token = new byte[body.Length + mac.Length];
                Buffer.BlockCopy(body, 0, token, 0, body.Length);
                Buffer.BlockCopy(mac, 0, token, body.Length, mac.Length);
                return Base64Url(token);
            }
        }

        /// <summary>
        /// Validate a token and return its payload when authentic and unexpired.
        /// </summary>
        /// <param name="token">Token.</param>
        /// <returns>Payload or null.</returns>
        public TokenPayload? Validate(string? token)
        {
            if (String.IsNullOrEmpty(token)) return null;
            try
            {
                if (_Cipher != null)
                {
                    if (!token.StartsWith("hugo1.", StringComparison.Ordinal)) return _Legacy?.Validate(token);
                    string plaintext = _Cipher.Decrypt(Constants.KeyPurposeSessions, token.Substring(6), _Issuer);
                    TokenPayload? decoded = JsonSerializer.Deserialize<TokenPayload>(plaintext, Serializer.Options);
                    return decoded != null && decoded.Issuer == _Issuer && decoded.ExpiresUtc >= DateTime.UtcNow ? decoded : null;
                }
                byte[] raw = FromBase64Url(token);
                if (raw.Length < 16 + 16 + 32) return null;

                byte[] body = new byte[raw.Length - 32];
                byte[] mac = new byte[32];
                Buffer.BlockCopy(raw, 0, body, 0, body.Length);
                Buffer.BlockCopy(raw, body.Length, mac, 0, 32);
                byte[] expected = HMACSHA256.HashData(_MacKey, body);
                if (!CryptographicOperations.FixedTimeEquals(mac, expected)) return null;

                byte[] iv = new byte[16];
                byte[] cipher = new byte[body.Length - 16];
                Buffer.BlockCopy(body, 0, iv, 0, 16);
                Buffer.BlockCopy(body, 16, cipher, 0, cipher.Length);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = _EncryptionKey;
                    byte[] plaintext = aes.DecryptCbc(cipher, iv);
                    TokenPayload? payload = JsonSerializer.Deserialize<TokenPayload>(plaintext, Serializer.Options);
                    if (payload == null) return null;
                    if (payload.Issuer != _Issuer) return null;
                    if (payload.ExpiresUtc < DateTime.UtcNow) return null;
                    return payload;
                }
            }
            catch (FormatException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string text)
        {
            string s = text.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        #endregion
    }
}
