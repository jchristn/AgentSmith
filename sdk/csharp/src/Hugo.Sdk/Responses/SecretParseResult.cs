namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Description of parsed key material.
    /// </summary>
    public class SecretParseResult
    {
        #region Public-Members

        /// <summary>
        /// Detected kind.
        /// </summary>
        public SecretKindEnum Kind { get; set; } = SecretKindEnum.Generic;

        /// <summary>
        /// Format.
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Suggested field.
        /// </summary>
        public string? SuggestedField { get; set; } = null;

        /// <summary>
        /// Fields.
        /// </summary>
        public List<string> Fields { get; set; } = new List<string>();

        /// <summary>
        /// Fingerprint.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// Key type.
        /// </summary>
        public string? KeyType { get; set; } = null;

        /// <summary>
        /// Key size bits.
        /// </summary>
        public int? KeySizeBits { get; set; } = null;

        /// <summary>
        /// Encrypted.
        /// </summary>
        public bool Encrypted { get; set; } = false;

        /// <summary>
        /// Comment.
        /// </summary>
        public string? Comment { get; set; } = null;

        /// <summary>
        /// Project Identifier.
        /// </summary>
        public string? ProjectId { get; set; } = null;

        /// <summary>
        /// Client email.
        /// </summary>
        public string? ClientEmail { get; set; } = null;

        /// <summary>
        /// Private key Identifier.
        /// </summary>
        public string? PrivateKeyId { get; set; } = null;

        /// <summary>
        /// Contexts.
        /// </summary>
        public List<KubeconfigContext> Contexts { get; set; } = new List<KubeconfigContext>();

        /// <summary>
        /// Current context.
        /// </summary>
        public string? CurrentContext { get; set; } = null;

        /// <summary>
        /// Subject.
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Issuer.
        /// </summary>
        public string? Issuer { get; set; } = null;

        /// <summary>
        /// Not before UTC.
        /// </summary>
        public DateTime? NotBeforeUtc { get; set; } = null;

        /// <summary>
        /// Not after UTC.
        /// </summary>
        public DateTime? NotAfterUtc { get; set; } = null;

        /// <summary>
        /// Certificate count.
        /// </summary>
        public int? CertificateCount { get; set; } = null;

        /// <summary>
        /// Has private key.
        /// </summary>
        public bool HasPrivateKey { get; set; } = false;

        /// <summary>
        /// Warnings.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        #endregion
    }
}
