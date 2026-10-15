namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Revocable authentication session backing an opaque token.
    /// </summary>
    public class AuthSession
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewAuthSessionId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier. Null for global or built-in records.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier for user sessions.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier for credential sessions.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Principal type.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Display name of the principal (the user's email, the credential's name, or the administrator label). Filled
        /// by the session routes for display; not stored. Null when the principal no longer exists.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Scheme that created the session.
        /// </summary>
        public AuthSchemeEnum AuthScheme { get; set; } = AuthSchemeEnum.BearerToken;

        /// <summary>
        /// Random token identifier embedded in the token.
        /// </summary>
        [JsonIgnore]
        public string TokenId
        {
            get
            {
                return _TokenId;
            }
            set
            {
                _TokenId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Source IP at issuance.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent at issuance.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// Expiration timestamp (UTC).
        /// </summary>
        public DateTime ExpiresUtc
        {
            get
            {
                return _ExpiresUtc;
            }
            set
            {
                _ExpiresUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last use timestamp (UTC).
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// Revocation timestamp (UTC).
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Revocation reason.
        /// </summary>
        public string? RevocationReason { get; set; } = null;

        /// <summary>
        /// Last time the session holder re-entered the password (POST /v1.0/token/reauthenticate), or null. Secret reveal
        /// requires this within Secrets.RevealReauthMinutes.
        /// </summary>
        public DateTime? ReauthenticatedUtc { get; set; } = null;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether the record is protected from deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewAuthSessionId();
        private string _TokenId = String.Empty;
        private DateTime _ExpiresUtc = DateTime.UtcNow;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
