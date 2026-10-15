namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Secrets vault settings (plan section 12). The master key itself should come from HUGO_MASTER_KEY; the settings key and key file are for development only.
    /// </summary>
    public class SecretsSettings
    {
        #region Public-Members

        /// <summary>
        /// Development-only master key (base64, at least 32 bytes). Null by default. HUGO_MASTER_KEY takes precedence; a key here produces a loud startup warning.
        /// </summary>
        public string? MasterKey { get; set; } = null;

        /// <summary>
        /// Development-only key file used when no other key is configured; created with a random key when missing. Default data/master.key. Null or empty disables it, and then a missing HUGO_MASTER_KEY stops startup.
        /// </summary>
        public string? MasterKeyFile { get; set; } = "data/master.key";

        /// <summary>
        /// Version of the current master key (HUGO_MASTER_KEY_VERSION). Increase it when moving to a new key. Minimum 1, maximum 255, default 1.
        /// </summary>
        public int MasterKeyVersion
        {
            get
            {
                return _MasterKeyVersion;
            }
            set
            {
                _MasterKeyVersion = Math.Clamp(value, 1, 255);
            }
        }

        /// <summary>
        /// Version of HUGO_MASTER_KEY_PREVIOUS. 0 (default) means MasterKeyVersion minus 1. Maximum 255.
        /// </summary>
        public int PreviousMasterKeyVersion
        {
            get
            {
                return _PreviousMasterKeyVersion;
            }
            set
            {
                _PreviousMasterKeyVersion = Math.Clamp(value, 0, 255);
            }
        }

        /// <summary>
        /// Minutes after POST /v1.0/token/reauthenticate during which the session may reveal secrets. Minimum 1, maximum 60, default 5.
        /// </summary>
        public int RevealReauthMinutes
        {
            get
            {
                return _RevealReauthMinutes;
            }
            set
            {
                _RevealReauthMinutes = Math.Clamp(value, 1, 60);
            }
        }

        /// <summary>
        /// Reveals per source IP and per principal per minute. Minimum 1, maximum 1000, default 10.
        /// </summary>
        public int RevealRateLimitPerMinute
        {
            get
            {
                return _RevealRateLimitPerMinute;
            }
            set
            {
                _RevealRateLimitPerMinute = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Rows re-encrypted per transaction by the master key rotation job. Minimum 1, maximum 5000, default 200.
        /// </summary>
        public int RotationBatchSize
        {
            get
            {
                return _RotationBatchSize;
            }
            set
            {
                _RotationBatchSize = Math.Clamp(value, 1, 5000);
            }
        }

        #endregion

        #region Private-Members

        private int _MasterKeyVersion = 1;
        private int _PreviousMasterKeyVersion = 0;
        private int _RevealReauthMinutes = 5;
        private int _RevealRateLimitPerMinute = 10;
        private int _RotationBatchSize = 200;

        #endregion
    }
}
