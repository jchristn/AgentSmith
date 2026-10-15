namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Non-interactive principal (access key and secret) owned by a user.
    /// </summary>
    public class Credential
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
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewCredentialId() : value;
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
        /// Owning user identifier.
        /// </summary>
        public string UserId
        {
            get
            {
                return _UserId;
            }
            set
            {
                _UserId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Human-readable name.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Public access key.
        /// </summary>
        public string AccessKey
        {
            get
            {
                return _AccessKey;
            }
            set
            {
                _AccessKey = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Encrypted secret key material. Never returned by the API.
        /// </summary>
        [JsonIgnore]
        public string SecretKeyEncrypted
        {
            get
            {
                return _SecretKeyEncrypted;
            }
            set
            {
                _SecretKeyEncrypted = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Last four characters of the secret key.
        /// </summary>
        public string? SecretKeyLast4 { get; set; } = null;

        /// <summary>
        /// Authentication mode.
        /// </summary>
        public CredentialAuthModeEnum AuthMode { get; set; } = CredentialAuthModeEnum.Hybrid;

        /// <summary>
        /// Agent profile the credential is bound to, or null (D-018).
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Whether the credential can act only as its bound agent profile. Default false.
        /// </summary>
        public bool LockToProfile { get; set; } = false;

        /// <summary>
        /// Last use timestamp (UTC).
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// Expiration timestamp (UTC).
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

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

        private string _Id = IdGenerator.NewCredentialId();
        private string _TenantId = String.Empty;
        private string _UserId = String.Empty;
        private string _Name = String.Empty;
        private string _AccessKey = String.Empty;
        private string _SecretKeyEncrypted = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
