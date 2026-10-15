namespace Hugo.Connectors.Storage
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Provider-neutral limits and safety settings of one storage resource. Not thread-safe for mutation.
    /// </summary>
    public class StorageOptions
    {
        #region Public-Members

        /// <summary>
        /// Prefix every object key must start with. Empty means no restriction. Never null.
        /// </summary>
        public string PrefixRestriction
        {
            get
            {
                return _PrefixRestriction;
            }
            set
            {
                _PrefixRestriction = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Maximum bytes of object content returned by one read. Minimum 1024, maximum 16 MiB, default 1 MiB.
        /// </summary>
        public int MaxObjectReadBytes
        {
            get
            {
                return _MaxObjectReadBytes;
            }
            set
            {
                _MaxObjectReadBytes = Math.Clamp(value, MinimumReadBytes, MaximumReadBytes);
            }
        }

        /// <summary>
        /// Maximum entries returned by one list call. Minimum 1, maximum 10000, default 1000.
        /// </summary>
        public int MaxListResults
        {
            get
            {
                return _MaxListResults;
            }
            set
            {
                _MaxListResults = Math.Clamp(value, 1, MaximumListResults);
            }
        }

        /// <summary>
        /// Maximum objects examined by one stats call or one folder-style listing. Minimum 100, maximum 10000000,
        /// default 100000.
        /// </summary>
        public int MaxScanObjects
        {
            get
            {
                return _MaxScanObjects;
            }
            set
            {
                _MaxScanObjects = Math.Clamp(value, MinimumScanObjects, MaximumScanObjects);
            }
        }

        #endregion

        #region Internal-Members

        internal const int MinimumReadBytes = 1024;
        internal const int MaximumReadBytes = 16 * 1024 * 1024;
        internal const int DefaultReadBytes = 1024 * 1024;
        internal const int MaximumListResults = 10000;
        internal const int DefaultListResults = 1000;
        internal const int MinimumScanObjects = 100;
        internal const int MaximumScanObjects = 10000000;
        internal const int DefaultScanObjects = 100000;

        #endregion

        #region Private-Members

        private string _PrefixRestriction = String.Empty;
        private int _MaxObjectReadBytes = DefaultReadBytes;
        private int _MaxListResults = DefaultListResults;
        private int _MaxScanObjects = DefaultScanObjects;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the options from resource settings, applying defaults and clamping to the documented ranges.
        /// </summary>
        /// <param name="settings">Resource settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static StorageOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            StorageOptions options = new StorageOptions();
            options.PrefixRestriction = (settings.GetString(StorageSettingKeys.PrefixRestriction) ?? String.Empty).Trim();
            options.MaxObjectReadBytes = settings.GetInt(StorageSettingKeys.MaxObjectReadBytes, DefaultReadBytes, MinimumReadBytes, MaximumReadBytes);
            options.MaxListResults = settings.GetInt(StorageSettingKeys.MaxListResults, DefaultListResults, 1, MaximumListResults);
            options.MaxScanObjects = settings.GetInt(StorageSettingKeys.MaxScanObjects, DefaultScanObjects, MinimumScanObjects, MaximumScanObjects);
            return options;
        }

        #endregion
    }
}
