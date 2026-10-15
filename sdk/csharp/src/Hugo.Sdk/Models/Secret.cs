namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// Secret metadata. Values are never returned except by an explicit, audited reveal.
    /// </summary>
    public class Secret
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Owner user Identifier.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Secret name (unique per tenant).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Kind of secret material.
        /// </summary>
        public SecretKindEnum Kind { get; set; } = SecretKindEnum.Generic;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Master key version that encrypts the values.
        /// </summary>
        public int KeyVersion { get; set; } = 0;

        /// <summary>
        /// Non-reversible display hint, for example the last four characters.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// Last rotated UTC.
        /// </summary>
        public DateTime? LastRotatedUtc { get; set; } = null;

        /// <summary>
        /// Last revealed UTC.
        /// </summary>
        public DateTime? LastRevealedUtc { get; set; } = null;

        /// <summary>
        /// Expires UTC.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Field names, for example value or privateKey.
        /// </summary>
        public List<string> Fields { get; set; } = new List<string>();

        /// <summary>
        /// Resources and model endpoints that use the secret (on single reads).
        /// </summary>
        public List<SecretReferrer> Referrers { get; set; } = new List<SecretReferrer>();

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = false;

        /// <summary>
        /// Whether the record is protected from deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
