namespace Hugo.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Binds a connector secret slot of a resource to a vault secret (resource_secret_bindings, plan section 12.4). Not
    /// thread-safe.
    /// </summary>
    public class ResourceSecretBinding
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix rss_).
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
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewResourceSecretBindingId() : value;
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
        /// Slot name declared by the connector (for example privateKey).
        /// </summary>
        public string SlotName
        {
            get
            {
                return _SlotName;
            }
            set
            {
                _SlotName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Vault secret identifier.
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
        /// Optional single field of the secret to expose in the slot. Null exposes every field of the secret
        /// under its own name, which is what connectors expect.
        /// </summary>
        public string? SecretFieldName { get; set; } = null;

        /// <summary>
        /// Secret name for display. Populated on read; not persisted.
        /// </summary>
        public string? SecretName { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewResourceSecretBindingId();
        private string _ResourceId = String.Empty;
        private string _TenantId = String.Empty;
        private string _SlotName = String.Empty;
        private string _SecretId = String.Empty;

        #endregion
    }
}
