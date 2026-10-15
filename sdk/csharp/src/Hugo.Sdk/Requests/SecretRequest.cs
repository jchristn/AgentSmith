namespace Hugo.Sdk.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// Create or update a secret. Values are accepted on create only; use SetValuesAsync to change them.
    /// </summary>
    public class SecretRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind.
        /// </summary>
        public SecretKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Field values by field name, for example value, or privateKey and passphrase.
        /// </summary>
        public Dictionary<string, string>? Values { get; set; } = null;

        /// <summary>
        /// Expires UTC.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        #endregion
    }
}
