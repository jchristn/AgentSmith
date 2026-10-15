namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// What an uploaded file contains (P2.8). Never carries the secret material itself: the client already holds the file
    /// and submits it as field values when it saves the secret.
    /// </summary>
    public class SecretImportResult
    {
        #region Public-Members

        /// <summary>
        /// Detected secret kind.
        /// </summary>
        public SecretKindEnum Kind { get; set; } = SecretKindEnum.Generic;

        /// <summary>
        /// Detected format, for example OpenSSH, PKCS#1, PKCS#8, SEC1, GcpServiceAccount, Kubeconfig, or X509.
        /// </summary>
        public string Format { get; set; } = String.Empty;

        /// <summary>
        /// Suggested field name for the uploaded content (for example privateKey, json, kubeconfig, certificate).
        /// </summary>
        public string SuggestedField { get; set; } = String.Empty;

        /// <summary>
        /// Field names the secret of this kind should carry.
        /// </summary>
        public List<string> Fields { get; set; } = new List<string>();

        /// <summary>
        /// Display fingerprint: SHA256:... for SSH keys, the SHA-256 thumbprint for certificates, or null.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// Key algorithm, for example ssh-ed25519, ssh-rsa, or ecdsa-sha2-nistp256; null when not a key.
        /// </summary>
        public string? KeyType { get; set; } = null;

        /// <summary>
        /// Key size in bits, or null.
        /// </summary>
        public int? KeySizeBits { get; set; } = null;

        /// <summary>
        /// Whether the private key is passphrase protected.
        /// </summary>
        public bool Encrypted { get; set; } = false;

        /// <summary>
        /// Key comment (OpenSSH, unencrypted keys only), or null.
        /// </summary>
        public string? Comment { get; set; } = null;

        /// <summary>
        /// GCP project identifier, or null.
        /// </summary>
        public string? ProjectId { get; set; } = null;

        /// <summary>
        /// GCP service account email, or null.
        /// </summary>
        public string? ClientEmail { get; set; } = null;

        /// <summary>
        /// GCP private key identifier, or null.
        /// </summary>
        public string? PrivateKeyId { get; set; } = null;

        /// <summary>
        /// Kubeconfig contexts.
        /// </summary>
        public List<KubeconfigContextSummary> Contexts { get; set; } = new List<KubeconfigContextSummary>();

        /// <summary>
        /// Kubeconfig current context, or null.
        /// </summary>
        public string? CurrentContext { get; set; } = null;

        /// <summary>
        /// Certificate subject, or null.
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Certificate issuer, or null.
        /// </summary>
        public string? Issuer { get; set; } = null;

        /// <summary>
        /// Certificate validity start (UTC), or null.
        /// </summary>
        public DateTime? NotBeforeUtc { get; set; } = null;

        /// <summary>
        /// Certificate validity end (UTC), or null.
        /// </summary>
        public DateTime? NotAfterUtc { get; set; } = null;

        /// <summary>
        /// Number of certificates in the file.
        /// </summary>
        public int CertificateCount { get; set; } = 0;

        /// <summary>
        /// Whether the file also contains a private key.
        /// </summary>
        public bool HasPrivateKey { get; set; } = false;

        /// <summary>
        /// Non-fatal observations (for example an encrypted key without a passphrase, or an expired certificate).
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        #endregion
    }
}
