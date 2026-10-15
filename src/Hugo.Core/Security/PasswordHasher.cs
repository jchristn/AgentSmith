namespace Hugo.Core.Security
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Password hashing with PBKDF2-HMAC-SHA256 and a random 16-byte salt per password, plus constant-time comparison.
    /// Stored hashes use the versioned format <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c>, where salt and hash are
    /// base64 (the hash is 32 bytes). Hashes written by releases before G-2 (lowercase hex SHA-256 with no salt) still
    /// verify, and <see cref="NeedsRehash"/> reports them so the caller can replace them after a successful sign-in.
    /// Thread-safe.
    /// </summary>
    public static class PasswordHasher
    {
        #region Public-Members

        /// <summary>
        /// Prefix of the current hash format.
        /// </summary>
        public static readonly string FormatPrefix = "pbkdf2-sha256$";

        /// <summary>
        /// Iterations used when none are given: 600,000, the OWASP Password Storage Cheat Sheet figure for PBKDF2-HMAC-SHA256.
        /// </summary>
        public static readonly int DefaultIterations = 600000;

        /// <summary>
        /// Fewest iterations accepted when hashing: 10,000. Values below <see cref="DefaultIterations"/> are meant for tests.
        /// </summary>
        public static readonly int MinimumIterations = 10000;

        /// <summary>
        /// Most iterations accepted when hashing or verifying: 10,000,000.
        /// </summary>
        public static readonly int MaximumIterations = 10000000;

        /// <summary>
        /// Salt length in bytes: 16.
        /// </summary>
        public static readonly int SaltBytes = 16;

        /// <summary>
        /// Derived hash length in bytes: 32.
        /// </summary>
        public static readonly int HashBytes = 32;

        #endregion

        #region Private-Members

        private static readonly Lazy<string> _DummyHash = new Lazy<string>(() => Hash(SecretGenerator.Alphanumeric(32)));

        #endregion

        #region Public-Methods

        /// <summary>
        /// Hash a password with <see cref="DefaultIterations"/> iterations and a new random salt.
        /// </summary>
        /// <param name="password">Password.</param>
        /// <returns>Encoded hash in the <c>pbkdf2-sha256$</c> format.</returns>
        /// <exception cref="ArgumentNullException">Thrown when password is null.</exception>
        public static string Hash(string password)
        {
            return Hash(password, DefaultIterations);
        }

        /// <summary>
        /// Hash a password with a new random salt.
        /// </summary>
        /// <param name="password">Password.</param>
        /// <param name="iterations">PBKDF2 iterations, from <see cref="MinimumIterations"/> to <see cref="MaximumIterations"/>.</param>
        /// <returns>Encoded hash in the <c>pbkdf2-sha256$</c> format.</returns>
        /// <exception cref="ArgumentNullException">Thrown when password is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when iterations is outside the allowed range.</exception>
        public static string Hash(string password, int iterations)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            if (iterations < MinimumIterations || iterations > MaximumIterations)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), "PBKDF2 iterations must be between " + MinimumIterations + " and " + MaximumIterations + ".");
            }
            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
            return FormatPrefix + iterations.ToString(CultureInfo.InvariantCulture) + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Lowercase hex SHA-256 of a password with no salt: the format stored before G-2. Kept to verify and test legacy hashes; never use it to store a new password.
        /// </summary>
        /// <param name="password">Password.</param>
        /// <returns>Lowercase hex SHA-256.</returns>
        /// <exception cref="ArgumentNullException">Thrown when password is null.</exception>
        public static string LegacySha256(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
        }

        /// <summary>
        /// Verify a password against a stored hash in constant time. Accepts the current format and legacy unsalted SHA-256 hex.
        /// </summary>
        /// <param name="password">Candidate password.</param>
        /// <param name="stored">Stored hash.</param>
        /// <returns>True when they match; false for a null password, an empty or malformed hash, or a mismatch.</returns>
        public static bool Verify(string? password, string? stored)
        {
            if (password == null || String.IsNullOrEmpty(stored)) return false;
            if (stored.StartsWith(FormatPrefix, StringComparison.Ordinal))
            {
                string[] parts = stored.Split('$');
                if (parts.Length != 4) return false;
                int iterations;
                if (!Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)) return false;
                if (iterations < 1 || iterations > MaximumIterations) return false;
                byte[] salt;
                byte[] expected;
                try
                {
                    salt = Convert.FromBase64String(parts[2]);
                    expected = Convert.FromBase64String(parts[3]);
                }
                catch (FormatException)
                {
                    return false;
                }
                if (salt.Length == 0 || expected.Length == 0) return false;
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            if (IsLegacy(stored)) return FixedTimeEquals(LegacySha256(password), stored.ToLowerInvariant());
            return false;
        }

        /// <summary>
        /// Spend about as long as one verification of a current-format hash, then return false. Call it when the user is
        /// unknown or inactive, so sign-in timing does not reveal which email addresses exist.
        /// </summary>
        /// <param name="password">Candidate password; null is treated as empty.</param>
        /// <returns>Always false.</returns>
        public static bool VerifyDummy(string? password)
        {
            Verify(password ?? String.Empty, _DummyHash.Value);
            return false;
        }

        /// <summary>
        /// Whether a stored hash should be replaced: true for legacy unsalted SHA-256 hashes and for current-format hashes
        /// with fewer iterations than <paramref name="iterations"/>. Hashes with more iterations are kept.
        /// </summary>
        /// <param name="stored">Stored hash.</param>
        /// <param name="iterations">Iterations the deployment now uses.</param>
        /// <returns>True when the hash should be recomputed after a successful verification.</returns>
        public static bool NeedsRehash(string? stored, int iterations)
        {
            if (String.IsNullOrEmpty(stored)) return false;
            if (IsLegacy(stored)) return true;
            if (!stored.StartsWith(FormatPrefix, StringComparison.Ordinal)) return false;
            string[] parts = stored.Split('$');
            if (parts.Length != 4) return false;
            int current;
            if (!Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out current)) return false;
            return current < iterations;
        }

        /// <summary>
        /// Whether a stored hash is in the legacy format (64 hex characters, unsalted SHA-256).
        /// </summary>
        /// <param name="stored">Stored hash.</param>
        /// <returns>True for a legacy hash.</returns>
        public static bool IsLegacy(string? stored)
        {
            if (stored == null || stored.Length != 64) return false;
            foreach (char c in stored)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }
            return true;
        }

        /// <summary>
        /// Constant-time string comparison (the time depends only on the lengths).
        /// </summary>
        /// <param name="a">First value.</param>
        /// <param name="b">Second value.</param>
        /// <returns>True when equal.</returns>
        public static bool FixedTimeEquals(string? a, string? b)
        {
            if (a == null || b == null) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        }

        #endregion
    }
}
