namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Helpers;

    /// <summary>
    /// One encrypted field of a secret (table secret_values). The ciphertext is bound to tenantId|secretId|fieldName as
    /// AES-GCM associated data, so a ciphertext copied to another row or tenant fails to decrypt. Not thread-safe.
    /// </summary>
    public class SecretValue
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix scv_). An empty value generates a new identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewSecretValueId() : value;
            }
        }

        /// <summary>
        /// Owning secret identifier.
        /// </summary>
        public string SecretId
        {
            get
            {
                return _SecretId;
            }
            set
            {
                _SecretId = value ?? String.Empty;
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
        /// Field name, for example password, privateKey, or secretAccessKey. Maximum 128 characters.
        /// </summary>
        public string FieldName
        {
            get
            {
                return _FieldName;
            }
            set
            {
                _FieldName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Base64 ciphertext frame produced by the secret cipher.
        /// </summary>
        public string Ciphertext
        {
            get
            {
                return _Ciphertext;
            }
            set
            {
                _Ciphertext = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Master key version of the ciphertext. Minimum 1.
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

        private string _Id = IdGenerator.NewSecretValueId();
        private string _SecretId = String.Empty;
        private string _TenantId = String.Empty;
        private string _FieldName = String.Empty;
        private string _Ciphertext = String.Empty;
        private int _KeyVersion = 1;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
