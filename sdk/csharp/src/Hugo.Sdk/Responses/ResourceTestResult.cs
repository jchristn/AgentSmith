namespace Hugo.Sdk.Responses
{
    /// <summary>
    /// Result of a resource connection test (preview).
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

        #endregion
    }
}
