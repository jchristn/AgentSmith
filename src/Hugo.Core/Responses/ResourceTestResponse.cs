namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Result of testing a resource's settings (saved or unsaved). Not thread-safe.
    /// </summary>
    public class ResourceTestResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether validation and the connection probe succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Message (scrubbed).
        /// </summary>
        public string Message
        {
            get
            {
                return _Message;
            }
            set
            {
                _Message = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Stable error code, when it failed.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Probe latency in milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0;

        /// <summary>
        /// Probe details (scrubbed).
        /// </summary>
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Field-level validation errors.
        /// </summary>
        public List<ConnectorValidationError> ValidationErrors { get; set; } = new List<ConnectorValidationError>();

        /// <summary>
        /// Settings the connector learned (for example a pinned host key).
        /// </summary>
        public Dictionary<string, string> LearnedSettings { get; set; } = new Dictionary<string, string>();

        #endregion

        #region Private-Members

        private string _Message = String.Empty;

        #endregion
    }
}
