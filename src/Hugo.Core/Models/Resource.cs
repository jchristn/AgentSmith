namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A configured instance of a connector type (plan section 7.4): an SSH host, a bucket, a database, a log account.
    /// Settings live in the resource_settings child table (D-016) and secrets are referenced by slot
    /// (resource_secret_bindings). Not thread-safe.
    /// </summary>
    public class Resource
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix res_). An empty value generates a new identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewResourceId() : value;
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
        /// User who owns the resource, or null when created by a credential without an owner or the system.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Private (owner and administrators only) or Tenant. Default Tenant.
        /// </summary>
        public VisibilityEnum Visibility { get; set; } = VisibilityEnum.Tenant;

        /// <summary>
        /// Name, unique per tenant, used as the default binding alias. Lowercase letters, digits, hyphens, and
        /// underscores, 1 to 64 characters.
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
        /// Connector type (for example ssh, postgresql).
        /// </summary>
        public string ConnectorType
        {
            get
            {
                return _ConnectorType;
            }
            set
            {
                _ConnectorType = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Display name, or null.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Description shown to operators and in the compiled instructions, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Searchable endpoint summary (for example web-01.example.com:22), derived from the settings when not set.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Free environment tag (for example prod), or null.
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Operator notes injected into the compiled instructions, or null.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Highest access mode any binding of this resource may use, or null for no ceiling.
        /// </summary>
        public AccessModeEnum? DefaultAccessModeCeiling { get; set; } = null;

        /// <summary>
        /// When the connection was last tested (UTC), or null.
        /// </summary>
        public DateTime? LastTestUtc { get; set; } = null;

        /// <summary>
        /// Whether the last connection test succeeded, or null when never tested.
        /// </summary>
        public bool? LastTestResult { get; set; } = null;

        /// <summary>
        /// Message from the last connection test (scrubbed), or null.
        /// </summary>
        public string? LastTestMessage { get; set; } = null;

        /// <summary>
        /// Whether the resource is active. Inactive resources cannot be invoked.
        /// </summary>
        public bool Active { get; set; } = true;

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
        /// Settings, one per key, validated against the connector's settings schema.
        /// </summary>
        public List<ResourceSetting> Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                _Settings = value ?? new List<ResourceSetting>();
            }
        }

        /// <summary>
        /// Secret slot bindings.
        /// </summary>
        public List<ResourceSecretBinding> SecretBindings
        {
            get
            {
                return _SecretBindings;
            }
            set
            {
                _SecretBindings = value ?? new List<ResourceSecretBinding>();
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewResourceId();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;
        private string _ConnectorType = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;
        private List<ResourceSetting> _Settings = new List<ResourceSetting>();
        private List<ResourceSecretBinding> _SecretBindings = new List<ResourceSecretBinding>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Settings as a key to value map (string form).
        /// </summary>
        /// <returns>Map. Never null.</returns>
        public Dictionary<string, string> SettingValues()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ResourceSetting setting in _Settings)
            {
                if (String.IsNullOrEmpty(setting.Key)) continue;
                values[setting.Key] = setting.Value ?? String.Empty;
            }
            return values;
        }

        #endregion
    }
}
