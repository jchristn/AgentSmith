namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// MCP surface settings (used from Phase 7). MCP is served on the REST port at /mcp.
    /// </summary>
    public class McpSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the MCP surface is enabled. Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Loopback port of the internal MCP listener behind /mcp; 0 picks a free port. Maximum 65535, default 0.
        /// </summary>
        public int InternalPort
        {
            get
            {
                return _InternalPort;
            }
            set
            {
                _InternalPort = Math.Clamp(value, 0, 65535);
            }
        }

        /// <summary>
        /// Seconds an idle MCP session lives. Minimum 60, maximum 604800, default 900.
        /// </summary>
        public int SessionIdleTimeoutSeconds
        {
            get
            {
                return _SessionIdleTimeoutSeconds;
            }
            set
            {
                _SessionIdleTimeoutSeconds = Math.Clamp(value, 60, 604800);
            }
        }

        /// <summary>
        /// Largest MCP message in bytes. Minimum 1024, maximum 268435456, default 4194304.
        /// </summary>
        public int MaxMessageSize
        {
            get
            {
                return _MaxMessageSize;
            }
            set
            {
                _MaxMessageSize = Math.Clamp(value, 1024, 268435456);
            }
        }

        #endregion

        #region Private-Members

        private int _InternalPort = 0;
        private int _SessionIdleTimeoutSeconds = 900;
        private int _MaxMessageSize = 4194304;

        #endregion
    }
}
