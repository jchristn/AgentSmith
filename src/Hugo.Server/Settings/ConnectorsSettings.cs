namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Connector settings (used from Phase 3).
    /// </summary>
    public class ConnectorsSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether connector plugins load from PluginDirectory. Default false.
        /// </summary>
        public bool EnablePlugins { get; set; } = false;

        /// <summary>
        /// Whether resources of type mcp may launch local MCP servers over stdio inside the Hugo process environment.
        /// Each resource must also opt in with its allowStdio setting. Default false (restart required).
        /// </summary>
        public bool McpAllowStdio { get; set; } = false;

        /// <summary>
        /// Plugin directory. Default plugins.
        /// </summary>
        public string PluginDirectory { get; set; } = "plugins";

        /// <summary>
        /// Default tool timeout in seconds. Minimum 1, maximum 3600, default 120.
        /// </summary>
        public int DefaultToolTimeoutSeconds
        {
            get
            {
                return _DefaultToolTimeoutSeconds;
            }
            set
            {
                _DefaultToolTimeoutSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Seconds after which the server cancels a connection test (D-036). Default 30, minimum 1, maximum 600.
        /// </summary>
        public int TestTimeoutSeconds
        {
            get
            {
                return _TestTimeoutSeconds;
            }
            set
            {
                _TestTimeoutSeconds = Math.Clamp(value, 1, 600);
            }
        }

        /// <summary>
        /// Tool timeout in seconds for tools that call the profile's model (RequiresModel), which run longer than other tools.
        /// Default 600, minimum 1, maximum 3600.
        /// </summary>
        public int ModelToolTimeoutSeconds
        {
            get
            {
                return _ModelToolTimeoutSeconds;
            }
            set
            {
                _ModelToolTimeoutSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Concurrent tool calls allowed per resource; further calls wait. Default 4, minimum 1, maximum 256.
        /// </summary>
        public int MaxConcurrencyPerResource
        {
            get
            {
                return _MaxConcurrencyPerResource;
            }
            set
            {
                _MaxConcurrencyPerResource = Math.Clamp(value, 1, 256);
            }
        }

        /// <summary>
        /// Minutes an idle pooled connector session (SSH client, database connection) is kept. Default 10, minimum 1, maximum 1440.
        /// </summary>
        public int SessionIdleMinutes
        {
            get
            {
                return _SessionIdleMinutes;
            }
            set
            {
                _SessionIdleMinutes = Math.Clamp(value, 1, 1440);
            }
        }

        /// <summary>
        /// Model calls one tool invocation may make through IConnectorModel before further calls fail with rate_limited.
        /// Default 60, minimum 0 (no model calls), maximum 10000.
        /// </summary>
        public int ModelCallBudgetPerInvocation
        {
            get
            {
                return _ModelCallBudgetPerInvocation;
            }
            set
            {
                _ModelCallBudgetPerInvocation = Math.Clamp(value, 0, 10000);
            }
        }

        #endregion

        #region Private-Members

        private int _DefaultToolTimeoutSeconds = 120;
        private int _TestTimeoutSeconds = 30;
        private int _ModelToolTimeoutSeconds = 600;
        private int _MaxConcurrencyPerResource = 4;
        private int _SessionIdleMinutes = 10;
        private int _ModelCallBudgetPerInvocation = 60;

        #endregion
    }
}
