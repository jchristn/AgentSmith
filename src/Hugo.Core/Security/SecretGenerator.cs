namespace Hugo.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Generates access keys, secret keys, and random tokens from a cryptographic RNG. Thread-safe.
    /// </summary>
    public static class SecretGenerator
    {
        #region Private-Members

        private static readonly string _Alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Random alphanumeric string.
        /// </summary>
        /// <param name="length">Length. Minimum 1, maximum 4096.</param>
        /// <returns>Random string.</returns>
        public static string Alphanumeric(int length)
        {
            length = Math.Clamp(length, 1, 4096);
            StringBuilder sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(_Alphanumeric[RandomNumberGenerator.GetInt32(_Alphanumeric.Length)]);
            return sb.ToString();
        }

        /// <summary>
        /// New access key: access_ followed by 32 alphanumeric characters.
        /// </summary>
        /// <returns>Access key.</returns>
        public static string NewAccessKey()
        {
            return "access_" + Alphanumeric(32);
        }

        /// <summary>
        /// New secret key: secret_ followed by 48 alphanumeric characters.
        /// </summary>
        /// <returns>Secret key.</returns>
        public static string NewSecretKey()
        {
            return "secret_" + Alphanumeric(48);
        }

        /// <summary>
        /// Last four characters of a value, for redacted display.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Last four characters, or the whole value when shorter.</returns>
        public static string Last4(string? value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            return value.Length <= 4 ? value : value.Substring(value.Length - 4);
        }

        /// <summary>
        /// SHA-256 fingerprint prefix of a value (first 16 hex characters).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Fingerprint.</returns>
        public static string Fingerprint(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).Substring(0, 16).ToLowerInvariant();
        }

        #endregion
    }
}
