namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Helpers for SHA256 host key fingerprints in OpenSSH format (<c>SHA256:</c> followed by 43 characters of unpadded
    /// base64, as printed by <c>ssh-keygen -lf</c>). Thread-safe.
    /// </summary>
    public static class SshFingerprint
    {
        #region Private-Members

        private static readonly Regex _Base64Body = new Regex("^[A-Za-z0-9+/]{43}$", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a fingerprint to <c>SHA256:xxxx</c>. Accepts the value with or without the <c>SHA256:</c> prefix
        /// (case-insensitive) and with or without base64 padding.
        /// </summary>
        /// <param name="value">Fingerprint text. Null or blank returns null.</param>
        /// <returns>Normalized fingerprint, or null when the value is blank or not a SHA256 fingerprint.</returns>
        public static string? Normalize(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            if (v.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)) v = v.Substring(7);
            v = v.TrimEnd('=');
            if (!_Base64Body.IsMatch(v)) return null;
            return "SHA256:" + v;
        }

        /// <summary>
        /// Whether a value is a valid SHA256 fingerprint.
        /// </summary>
        /// <param name="value">Fingerprint text.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValid(string? value)
        {
            return Normalize(value) != null;
        }

        /// <summary>
        /// Compare two fingerprints after normalization. Base64 is case-sensitive.
        /// </summary>
        /// <param name="a">First fingerprint.</param>
        /// <param name="b">Second fingerprint.</param>
        /// <returns>True when both are valid and equal.</returns>
        public static bool AreEqual(string? a, string? b)
        {
            string? na = Normalize(a);
            string? nb = Normalize(b);
            return na != null && nb != null && String.Equals(na, nb, StringComparison.Ordinal);
        }

        #endregion
    }
}
