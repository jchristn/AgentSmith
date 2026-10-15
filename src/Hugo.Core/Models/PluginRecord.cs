namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A connector plugin assembly seen by the plugin loader (plugins, plan section 8.2). Global; system administrators
    /// manage plugins. Not thread-safe.
    /// </summary>
    public class PluginRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix plg_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewPluginId() : value;
            }
        }

        /// <summary>
        /// Plugin directory name.
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
        /// Assembly version.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Assembly path.
        /// </summary>
        public string AssemblyPath
        {
            get
            {
                return _AssemblyPath;
            }
            set
            {
                _AssemblyPath = value ?? String.Empty;
            }
        }

        /// <summary>
        /// SHA-256 of the assembly file.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Whether the plugin may load. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether the plugin is loaded in this process.
        /// </summary>
        public bool Loaded { get; set; } = false;

        /// <summary>
        /// Why loading failed, or null.
        /// </summary>
        public string? LoadError { get; set; } = null;

        /// <summary>
        /// Connector types the plugin registered.
        /// </summary>
        public List<string> ConnectorTypes
        {
            get
            {
                return _ConnectorTypes;
            }
            set
            {
                _ConnectorTypes = value ?? new List<string>();
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

        private string _Id = IdGenerator.NewPluginId();
        private string _Name = String.Empty;
        private string _AssemblyPath = String.Empty;
        private List<string> _ConnectorTypes = new List<string>();
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
