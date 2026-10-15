namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A master key version and its canary ciphertext (table master_key_versions). At startup the server decrypts the
    /// canary of the current version; when no configured key can, and encrypted rows exist, it refuses to start.
    /// </summary>
    public class MasterKeyVersion
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix mkv_). An empty value generates a new identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewMasterKeyVersionId() : value;
            }
        }

        /// <summary>
        /// Key version. Minimum 1, maximum 255 (one byte in the ciphertext frame).
        /// </summary>
        public int KeyVersion
        {
            get
            {
                return _KeyVersion;
            }
            set
            {
                _KeyVersion = Math.Clamp(value, 1, 255);
            }
        }

        /// <summary>
        /// Canary ciphertext: a known plaintext encrypted under this key version.
        /// </summary>
        public string Canary
        {
            get
            {
                return _Canary;
            }
            set
            {
                _Canary = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Whether this is the version new writes use.
        /// </summary>
        public bool IsCurrent { get; set; } = false;

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

        private string _Id = IdGenerator.NewMasterKeyVersionId();
        private int _KeyVersion = 1;
        private string _Canary = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
