namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A rebuildable connector cache entry (connector_schema_cache, plan section 7.5), for example a SQL schema crawl. Not
    /// thread-safe.
    /// </summary>
    public class ConnectorSchemaCacheEntry
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix csc_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewConnectorSchemaCacheId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
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
        /// Resource identifier.
        /// </summary>
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
        /// Cache kind (for example schema).
        /// </summary>
        public string CacheKind
        {
            get
            {
                return _CacheKind;
            }
            set
            {
                _CacheKind = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Cached content.
        /// </summary>
        public string Content
        {
            get
            {
                return _Content;
            }
            set
            {
                _Content = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc
        {
            get
            {
                return _ExpiresUtc;
            }
            set
            {
                _ExpiresUtc = TimestampHelper.Normalize(value);
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

        private string _Id = IdGenerator.NewConnectorSchemaCacheId();
        private string _TenantId = String.Empty;
        private string _ResourceId = String.Empty;
        private string _CacheKind = String.Empty;
        private string _Content = String.Empty;
        private DateTime _ExpiresUtc = DateTime.UtcNow;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
