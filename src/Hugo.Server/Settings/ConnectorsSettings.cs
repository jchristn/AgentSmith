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
        /// Minutes between resource health sweeps; 0 turns them off. Maximum 1440, default 15.
        /// </summary>
        public int HealthSweepMinutes
        {
            get
            {
                return _HealthSweepMinutes;
            }
            set
            {
                _HealthSweepMinutes = Math.Clamp(value, 0, 1440);
            }
        }

        /// <summary>
        /// Whether external MCP servers may use the stdio transport. Default false.
        /// </summary>
        public bool AllowStdioMcp { get; set; } = false;

        #endregion

        #region Private-Members

        private int _DefaultToolTimeoutSeconds = 120;
        private int _HealthSweepMinutes = 15;

        #endregion
    }
}
