namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A connector as returned by GET /v1.0/api/connectors: descriptor, settings schema, secret slots, and tools, so the
    /// dashboard and SDKs need no connector-specific code. Not thread-safe.
    /// </summary>
    public class ConnectorView
    {
        #region Public-Members

        /// <summary>
        /// Type.
        /// </summary>
        public string Type
        {
            get
            {
                return _Type;
            }
            set
            {
                _Type = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Family.
        /// </summary>
        public string Family
        {
            get
            {
                return _Family;
            }
            set
            {
                _Family = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName
        {
            get
            {
                return _DisplayName;
            }
            set
            {
                _DisplayName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Description.
        /// </summary>
        public string Description
        {
            get
            {
                return _Description;
            }
            set
            {
                _Description = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Category.
        /// </summary>
        public ConnectorCategoryEnum Category { get; set; } = ConnectorCategoryEnum.Other;

        /// <summary>
        /// Icon key.
        /// </summary>
        public string IconKey
        {
            get
            {
                return _IconKey;
            }
            set
            {
                _IconKey = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Version.
        /// </summary>
        public string Version
        {
            get
            {
                return _Version;
            }
            set
            {
                _Version = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Documentation URL.
        /// </summary>
        public string? DocumentationUrl { get; set; } = null;

        /// <summary>
        /// Prompt template key of its default instructions.
        /// </summary>
        public string DefaultInstructionsKey
        {
            get
            {
                return _DefaultInstructionsKey;
            }
            set
            {
                _DefaultInstructionsKey = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Whether it was loaded from a plugin.
        /// </summary>
        public bool Plugin { get; set; } = false;

        /// <summary>
        /// Settings schema.
        /// </summary>
        public List<SettingDescriptor> Settings { get; set; } = new List<SettingDescriptor>();

        /// <summary>
        /// Secret slots.
        /// </summary>
        public List<SecretSlotDescriptor> SecretSlots { get; set; } = new List<SecretSlotDescriptor>();

        /// <summary>
        /// Tools.
        /// </summary>
        public List<ConnectorToolView> Tools { get; set; } = new List<ConnectorToolView>();

        #endregion

        #region Private-Members

        private string _Type = String.Empty;
        private string _Family = String.Empty;
        private string _DisplayName = String.Empty;
        private string _Description = String.Empty;
        private string _IconKey = String.Empty;
        private string _Version = String.Empty;
        private string _DefaultInstructionsKey = String.Empty;

        #endregion
    }
}
