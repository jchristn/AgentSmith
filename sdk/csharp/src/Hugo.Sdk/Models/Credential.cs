namespace Hugo.Sdk.Models
{
    using System;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// An API credential (access key and secret key) owned by a user. The secret key is returned only at creation and rotation.
    /// </summary>
    public class Credential
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
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Access key (public part).
        /// </summary>
        public string AccessKey { get; set; } = String.Empty;

        /// <summary>
        /// Last four characters of the secret key.
        /// </summary>
        public string? SecretKeyLast4 { get; set; } = null;

        /// <summary>
        /// Allowed authentication mode.
        /// </summary>
        public CredentialAuthModeEnum AuthMode { get; set; } = CredentialAuthModeEnum.DirectHeader;

        /// <summary>
        /// Agent profile the credential is tied to, if any.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Whether the credential can only use its agent profile.
        /// </summary>
        public bool LockToProfile { get; set; } = false;

        /// <summary>
        /// Last used UTC.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// Expiry, UTC; null never expires.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

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
