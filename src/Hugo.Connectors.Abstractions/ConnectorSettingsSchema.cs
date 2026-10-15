namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Ordered list of settings a connector accepts. Treat as immutable after construction.
    /// </summary>
    public class ConnectorSettingsSchema
    {
        #region Public-Members

        /// <summary>
        /// Settings in display order. Never null.
        /// </summary>
        public List<SettingDescriptor> Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                _Settings = value ?? new List<SettingDescriptor>();
            }
        }

        #endregion

        #region Private-Members

        private List<SettingDescriptor> _Settings = new List<SettingDescriptor>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a setting and return this schema for chaining.
        /// </summary>
        /// <param name="setting">Setting descriptor.</param>
        /// <returns>This schema.</returns>
        /// <exception cref="ArgumentNullException">Thrown when setting is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a setting with the same key already exists.</exception>
        public ConnectorSettingsSchema Add(SettingDescriptor setting)
        {
            ArgumentNullException.ThrowIfNull(setting);
            if (_Settings.Any(s => String.Equals(s.Key, setting.Key, StringComparison.Ordinal)))
                throw new ArgumentException("Duplicate setting key '" + setting.Key + "'.", nameof(setting));
            _Settings.Add(setting);
            return this;
        }

        /// <summary>
        /// Find a setting by key.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <returns>The descriptor, or null when not found.</returns>
        public SettingDescriptor? Find(string key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            return _Settings.FirstOrDefault(s => String.Equals(s.Key, key, StringComparison.Ordinal));
        }

        #endregion
    }
}
