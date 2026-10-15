namespace Hugo.Connectors.Agents.Mcp
{
    using System;

    /// <summary>
    /// Process-wide options of the <c>mcp</c> connector, set by the host when it constructs the connector. Not thread-safe
    /// for mutation; set before the connector is used.
    /// </summary>
    public class McpConnectorOptions
    {
        #region Public-Members

        /// <summary>
        /// Server-level gate for the Stdio transport (Hugo's <c>Connectors.McpAllowStdio</c>). A Stdio resource runs only
        /// when this is true and the resource's own <c>allowStdio</c> setting is true. Default false, because a Stdio
        /// server is a process started inside the Hugo host.
        /// </summary>
        public bool AllowStdio { get; set; } = false;

        /// <summary>
        /// Client name reported to remote servers in <c>initialize</c>. Default <c>Hugo</c>. Null or empty restores the default.
        /// </summary>
        public string ClientName
        {
            get
            {
                return _ClientName;
            }
            set
            {
                _ClientName = String.IsNullOrWhiteSpace(value) ? "Hugo" : value;
            }
        }

        /// <summary>
        /// Client version reported to remote servers in <c>initialize</c>. Default <c>1.0.0</c> (the connector version).
        /// Null or empty restores the default.
        /// </summary>
        public string ClientVersion
        {
            get
            {
                return _ClientVersion;
            }
            set
            {
                _ClientVersion = String.IsNullOrWhiteSpace(value) ? "1.0.0" : value;
            }
        }

        /// <summary>
        /// Most resources whose discovered tool lists are kept for per-call classification. When full, the least recently
        /// refreshed entry is dropped (its next call classifies fail-closed until the list is fetched again). Minimum 1,
        /// maximum 100000, default 1000.
        /// </summary>
        public int CatalogCacheCapacity
        {
            get
            {
                return _CatalogCacheCapacity;
            }
            set
            {
                _CatalogCacheCapacity = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Most characters of one remote tool or resource description shown. Minimum 100, maximum 100000, default 1000.
        /// </summary>
        public int MaxDescriptionChars
        {
            get
            {
                return _MaxDescriptionChars;
            }
            set
            {
                _MaxDescriptionChars = Math.Clamp(value, 100, 100000);
            }
        }

        /// <summary>
        /// Most characters of one remote input schema shown. A longer schema is omitted with a note. Minimum 200,
        /// maximum 1000000, default 4000.
        /// </summary>
        public int MaxSchemaChars
        {
            get
            {
                return _MaxSchemaChars;
            }
            set
            {
                _MaxSchemaChars = Math.Clamp(value, 200, 1000000);
            }
        }

        /// <summary>
        /// Most characters of the remote server's <c>instructions</c> shown. Minimum 200, maximum 1000000, default 8000.
        /// </summary>
        public int MaxInstructionsChars
        {
            get
            {
                return _MaxInstructionsChars;
            }
            set
            {
                _MaxInstructionsChars = Math.Clamp(value, 200, 1000000);
            }
        }

        /// <summary>
        /// Most pages fetched when a remote list is paginated (<c>nextCursor</c>). Minimum 1, maximum 1000, default 20.
        /// </summary>
        public int MaxListPages
        {
            get
            {
                return _MaxListPages;
            }
            set
            {
                _MaxListPages = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Upper bound for <see cref="McpConnector.TestConnectionAsync"/>, applied together with the resource timeout and
        /// the server's own test timeout. Minimum 1 second, maximum 10 minutes, default 20 seconds.
        /// </summary>
        public TimeSpan TestConnectionTimeout
        {
            get
            {
                return _TestConnectionTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(10)) value = TimeSpan.FromMinutes(10);
                _TestConnectionTimeout = value;
            }
        }

        /// <summary>
        /// Discovered tool lists shared by the tools of one connector instance. Never null.
        /// </summary>
        public McpCatalogCache Catalogs
        {
            get
            {
                return _Catalogs;
            }
            set
            {
                _Catalogs = value ?? throw new ArgumentNullException(nameof(Catalogs));
            }
        }

        #endregion

        #region Private-Members

        private string _ClientName = "Hugo";
        private string _ClientVersion = "1.0.0";
        private int _CatalogCacheCapacity = 1000;
        private int _MaxDescriptionChars = 1000;
        private int _MaxSchemaChars = 4000;
        private int _MaxInstructionsChars = 8000;
        private int _MaxListPages = 20;
        private TimeSpan _TestConnectionTimeout = TimeSpan.FromSeconds(20);
        private McpCatalogCache _Catalogs = new McpCatalogCache();

        #endregion
    }
}
