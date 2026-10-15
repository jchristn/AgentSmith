namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// A resolved resource handed to a connector: identity, typed settings, and the secrets bound to its slots.
    /// Not thread-safe for mutation.
    /// </summary>
    public class ConnectorResourceContext
    {
        #region Public-Members

        /// <summary>
        /// Resource identifier (<c>res_...</c>). Empty for unsaved resources being tested.
        /// </summary>
        public string ResourceId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Resource name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Connector type.
        /// </summary>
        public string ConnectorType { get; set; } = String.Empty;

        /// <summary>
        /// Settings. Never null.
        /// </summary>
        public ResourceSettings Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                _Settings = value ?? new ResourceSettings(null);
            }
        }

        /// <summary>
        /// Secrets bound to slots. Never null.
        /// </summary>
        public SecretAccessor Secrets
        {
            get
            {
                return _Secrets;
            }
            set
            {
                _Secrets = value ?? new SecretAccessor();
            }
        }

        /// <summary>
        /// A stable hash of every setting and secret value, for session pool keys. Computed by the server.
        /// </summary>
        public string ConfigurationHash { get; set; } = String.Empty;

        /// <summary>
        /// Logger. Never null.
        /// </summary>
        public IConnectorLogger Logger
        {
            get
            {
                return _Logger;
            }
            set
            {
                _Logger = value ?? NullConnectorLogger.Instance;
            }
        }

        /// <summary>
        /// Session pool. Never null.
        /// </summary>
        public IConnectorSessionPool Sessions
        {
            get
            {
                return _Sessions;
            }
            set
            {
                _Sessions = value ?? throw new ArgumentNullException(nameof(Sessions));
            }
        }

        /// <summary>
        /// Recorder for values the connector learns and wants persisted on the resource (for example a pinned SSH host
        /// key fingerprint). Null when the caller does not persist learned values.
        /// </summary>
        public Action<string, string>? LearnSetting { get; set; } = null;

        #endregion

        #region Private-Members

        private ResourceSettings _Settings = new ResourceSettings(null);
        private SecretAccessor _Secrets = new SecretAccessor();
        private IConnectorLogger _Logger = NullConnectorLogger.Instance;
        private IConnectorSessionPool _Sessions = new NoPoolSessionPool();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Pool key for this resource and configuration, with an optional suffix.
        /// </summary>
        /// <param name="suffix">Optional suffix distinguishing session kinds.</param>
        /// <returns>Key.</returns>
        public string SessionKey(string? suffix = null)
        {
            string key = ConnectorType + ":" + (String.IsNullOrEmpty(ResourceId) ? Name : ResourceId) + ":" + ConfigurationHash;
            return String.IsNullOrEmpty(suffix) ? key : key + ":" + suffix;
        }

        #endregion
    }
}
