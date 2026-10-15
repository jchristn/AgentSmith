namespace Hugo.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Helpers;

    /// <summary>
    /// One setting of a resource (resource_settings, D-016). The value is stored in a typed column chosen by the value kind.
    /// Not thread-safe.
    /// </summary>
    public class ResourceSetting
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix rst_).
        /// </summary>
        [JsonIgnore]
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewResourceSettingId() : value;
            }
        }

        /// <summary>
        /// Resource identifier.
        /// </summary>
        [JsonIgnore]
        public string ResourceId
        {
            get
            {
                return _ResourceId;
            }
            set
            {
                _ResourceId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        [JsonIgnore]
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
        /// Setting key declared by the connector's settings schema.
        /// </summary>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                _Key = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Value kind. Integer values use the integer column, Boolean values the boolean column, everything else
        /// the string column (string lists newline separated).
        /// </summary>
        public SettingValueKindEnum ValueKind { get; set; } = SettingValueKindEnum.String;

        /// <summary>
        /// Value in string form.
        /// </summary>
        public string Value
        {
            get
            {
                return _Value;
            }
            set
            {
                _Value = value ?? String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewResourceSettingId();
        private string _ResourceId = String.Empty;
        private string _TenantId = String.Empty;
        private string _Key = String.Empty;
        private string _Value = String.Empty;

        #endregion
    }
}
