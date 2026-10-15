namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Result of a resource connection test.
    /// </summary>
    public class ResourceTestResult
    {
        #region Public-Members

        /// <summary>
        /// Success.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Error code.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Latency milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0;

        /// <summary>
        /// Connector-specific details.
        /// </summary>
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Settings validation errors.
        /// </summary>
        public List<ConnectorValidationError> ValidationErrors { get; set; } = new List<ConnectorValidationError>();

        /// <summary>
        /// Settings the connector learned during the test (for example a detected version).
        /// </summary>
        public Dictionary<string, string> LearnedSettings { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
