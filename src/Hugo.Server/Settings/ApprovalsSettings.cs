namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Approval settings (used from Phase 6).
    /// </summary>
    public class ApprovalsSettings
    {
        #region Public-Members

        /// <summary>
        /// Seconds before a pending approval expires. Minimum 30, maximum 604800, default 900.
        /// </summary>
        public int ExpirySeconds
        {
            get
            {
                return _ExpirySeconds;
            }
            set
            {
                _ExpirySeconds = Math.Clamp(value, 30, 604800);
            }
        }

        /// <summary>
        /// Seconds an MCP tool call waits for an approval before answering that approval is pending. Minimum 0, maximum 600, default 90.
        /// </summary>
        public int McpWaitSeconds
        {
            get
            {
                return _McpWaitSeconds;
            }
            set
            {
                _McpWaitSeconds = Math.Clamp(value, 0, 600);
            }
        }

        /// <summary>
        /// Whether the requester may approve their own request. Default true.
        /// </summary>
        public bool AllowSelfApproval { get; set; } = true;

        #endregion

        #region Private-Members

        private int _ExpirySeconds = 900;
        private int _McpWaitSeconds = 90;

        #endregion
    }
}
