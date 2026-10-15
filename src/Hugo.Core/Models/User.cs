namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// User within a tenant.
    /// </summary>
    public class User
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
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewUserId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Email address, unique within a tenant.
        /// </summary>
        public string Email
        {
            get
            {
                return _Email;
            }
            set
            {
                _Email = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Encoded password hash (see PasswordHasher): pbkdf2-sha256$iterations$salt$hash, or a legacy unsalted lowercase hex
        /// SHA-256 that is replaced on the next successful sign-in. Stored in the passwordsha256 column. Never returned by the API.
        /// </summary>
        [JsonIgnore]
        public string PasswordHash
        {
            get
            {
                return _PasswordHash;
            }
            set
            {
                _PasswordHash = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Whether the user is a system administrator (bypasses all checks).
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user is a tenant administrator (bypasses RBAC within the tenant).
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user must change the password before using any route other than password change, token
        /// details, and logout. Default false; seeded accounts start with true.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>
        /// Default agent profile identifier for the user, or null.
        /// </summary>
        public string? DefaultAgentProfileId { get; set; } = null;

        /// <summary>
        /// Preferred dashboard locale (for example en-US), or null for the browser default.
        /// </summary>
        public string? PreferredLocale { get; set; } = null;

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

        private string _Id = IdGenerator.NewUserId();
        private string _TenantId = String.Empty;
        private string _Email = String.Empty;
        private string _PasswordHash = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
