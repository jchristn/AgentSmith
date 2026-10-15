namespace Hugo.Core.Security
{
    using System;

    /// <summary>
    /// Where the secrets master key comes from (plan section 12.1). The environment is the production source; the
    /// settings file and the key file are for development only and produce a loud warning.
    /// </summary>
    public class MasterKeyOptions
    {
        #region Public-Members

        /// <summary>
        /// Current master key from HUGO_MASTER_KEY (base64, at least 32 bytes), or null.
        /// </summary>
        public string? EnvironmentKey { get; set; } = null;

        /// <summary>
        /// Previous master key from HUGO_MASTER_KEY_PREVIOUS (base64, at least 32 bytes), or null. Values written under
        /// it stay readable until rotation re-encrypts them.
        /// </summary>
        public string? EnvironmentPreviousKey { get; set; } = null;

        /// <summary>
        /// Development-only master key from the settings file (Secrets.MasterKey), or null.
        /// </summary>
        public string? SettingsKey { get; set; } = null;

        /// <summary>
        /// Development-only key file (Secrets.MasterKeyFile). When no other key is configured the file is read, or
        /// created with a random key. Null or empty disables it, and then a missing key is a startup error.
        /// </summary>
        public string? KeyFile { get; set; } = null;

        /// <summary>
        /// Version of the current key. Minimum 1, maximum 255, default 1.
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
        /// Version of the previous key. 0 (default) means one less than <see cref="KeyVersion"/>. Maximum 255.
        /// </summary>
        public int PreviousKeyVersion
        {
            get
            {
                return _PreviousKeyVersion;
            }
            set
            {
                _PreviousKeyVersion = Math.Clamp(value, 0, 255);
            }
        }

        #endregion

        #region Private-Members

        private int _KeyVersion = 1;
        private int _PreviousKeyVersion = 0;

        #endregion
    }
}
