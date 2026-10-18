namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Field names each secret kind accepts (see <see cref="SecretKindEnum"/>). Generic secrets accept any field name
    /// that matches [A-Za-z][A-Za-z0-9_.-]{0,127}. Thread-safe (immutable).
    /// </summary>
    public static class SecretFieldRules
    {
        #region Public-Members

        /// <summary>
        /// Placeholder the dashboard shows for stored values. A submitted value equal to it keeps the stored value and is
        /// never persisted.
        /// </summary>
        public static readonly string MaskPlaceholder = "********";

        /// <summary>
        /// Largest accepted field value, in characters (1 MiB).
        /// </summary>
        public static readonly int MaxValueLength = 1048576;

        /// <summary>
        /// Most fields one secret may hold.
        /// </summary>
        public static readonly int MaxFields = 32;

        #endregion

        #region Private-Members

        private static readonly Regex _FieldName = new Regex(@"^[A-Za-z][A-Za-z0-9_.\-]{0,127}$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fields a secret of the kind must hold.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Required field names.</returns>
        public static List<string> Required(SecretKindEnum kind)
        {
            switch (kind)
            {
                case SecretKindEnum.AesKey: return new List<string> { "key" };
                case SecretKindEnum.Password: return new List<string> { "password" };
                case SecretKindEnum.PrivateKey: return new List<string> { "privateKey" };
                case SecretKindEnum.ApiToken: return new List<string> { "token" };
                case SecretKindEnum.BearerToken: return new List<string> { "token" };
                case SecretKindEnum.AccessKeyPair: return new List<string> { "accessKeyId", "secretAccessKey" };
                case SecretKindEnum.ServiceAccountJson: return new List<string> { "json" };
                case SecretKindEnum.Kubeconfig: return new List<string> { "kubeconfig" };
                case SecretKindEnum.ConnectionString: return new List<string> { "connectionString" };
                case SecretKindEnum.Certificate: return new List<string> { "certificate" };
                default: return new List<string>();
            }
        }

        /// <summary>
        /// Fields a secret of the kind may additionally hold.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Optional field names (empty for Generic, which accepts any valid name).</returns>
        public static List<string> Optional(SecretKindEnum kind)
        {
            switch (kind)
            {
                case SecretKindEnum.AesKey: return new List<string> { "salt", "iv" };
                case SecretKindEnum.Password: return new List<string> { "username" };
                case SecretKindEnum.PrivateKey: return new List<string> { "passphrase" };
                case SecretKindEnum.AccessKeyPair: return new List<string> { "sessionToken" };
                case SecretKindEnum.Certificate: return new List<string> { "privateKey", "caCertificate" };
                default: return new List<string>();
            }
        }

        /// <summary>
        /// The field whose last four characters describe the secret when no better fingerprint exists.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Field name, or null for Generic and AES key material.</returns>
        public static string? PrimaryField(SecretKindEnum kind)
        {
            if (kind == SecretKindEnum.AesKey) return null;
            List<string> required = Required(kind);
            if (kind == SecretKindEnum.AccessKeyPair) return "accessKeyId";
            return required.FirstOrDefault();
        }

        /// <summary>
        /// Whether a field name is valid for the kind.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="fieldName">Field name.</param>
        /// <returns>True when accepted.</returns>
        public static bool Accepts(SecretKindEnum kind, string? fieldName)
        {
            if (String.IsNullOrEmpty(fieldName) || !_FieldName.IsMatch(fieldName)) return false;
            if (kind == SecretKindEnum.Generic) return true;
            return Required(kind).Contains(fieldName, StringComparer.Ordinal) || Optional(kind).Contains(fieldName, StringComparer.Ordinal);
        }

        #endregion
    }
}
