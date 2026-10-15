namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Plaintext values of a revealed secret.
    /// </summary>
    public class SecretRevealResponse
    {
        #region Public-Members

        /// <summary>
        /// Secret Identifier.
        /// </summary>
        public string SecretId { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Kind of secret material.
        /// </summary>
        public SecretKindEnum Kind { get; set; } = SecretKindEnum.Generic;

        /// <summary>
        /// Plaintext values by field name.
        /// </summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Revealed UTC.
        /// </summary>
        public DateTime RevealedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
