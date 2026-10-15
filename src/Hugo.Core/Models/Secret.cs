namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A secret in the vault: metadata plus one or more named, encrypted fields (plan section 12). Plaintext never
    /// leaves the vault except through the audited reveal operation. Not thread-safe.
    /// </summary>
    public class Secret
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix sec_). An empty value generates a new identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewSecretId() : value;
            }
        }

        /// <summary>
        /// Owning tenant identifier.
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
        /// User who created the secret, or null when created by an API key or the system.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Name, unique per tenant. Maximum 256 characters.
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
        /// Kind of credential material. Determines the accepted field names.
        /// </summary>
        public SecretKindEnum Kind { get; set; } = SecretKindEnum.Generic;

        /// <summary>
        /// Free-form description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Master key version the values were last written under. Minimum 1.
        /// </summary>
        public int KeyVersion
        {
            get
            {
                return _KeyVersion;
            }
            set
            {
                _KeyVersion = Math.Max(1, value);
            }
        }

        /// <summary>
        /// Non-reversible display hint: the SSH key fingerprint for private keys, a certificate thumbprint, or the last
        /// four characters of the primary field otherwise. Null when no value is stored.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// When the values were last written (UTC), or null.
        /// </summary>
        public DateTime? LastRotatedUtc { get; set; } = null;

        /// <summary>
        /// When the values were last revealed (UTC), or null.
        /// </summary>
        public DateTime? LastRevealedUtc { get; set; } = null;

        /// <summary>
        /// Optional expiry (UTC). Informational; expired secrets are flagged, not deleted.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether the record is protected from modification and deletion.
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

        /// <summary>
        /// Names of the fields that hold a value, ordered by name. Read-only view of <see cref="Values"/>.
        /// </summary>
        public List<string> Fields
        {
            get
            {
                if (_FieldNames != null) return _FieldNames;
                return _Values.Select(v => v.FieldName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            }
            set
            {
                _FieldNames = value;
            }
        }

        /// <summary>
        /// Resources and model endpoints that reference this secret. Populated only by the single-secret read route; null
        /// otherwise. Not persisted.
        /// </summary>
        public List<SecretReferrer>? Referrers { get; set; } = null;

        /// <summary>
        /// Encrypted field values. Never serialized.
        /// </summary>
        [JsonIgnore]
        public List<SecretValue> Values
        {
            get
            {
                return _Values;
            }
            set
            {
                _Values = value ?? new List<SecretValue>();
                _FieldNames = null;
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewSecretId();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;
        private int _KeyVersion = 1;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;
        private List<SecretValue> _Values = new List<SecretValue>();
        private List<string>? _FieldNames = null;

        #endregion
    }
}
