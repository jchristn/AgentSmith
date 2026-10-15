namespace Hugo.Sdk.Responses
{
    using System;

    /// <summary>
    /// One recorded health check of a model endpoint (validate or the server's health sweep).
    /// </summary>
    public class ModelEndpointHealthRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Model endpoint identifier.
        /// </summary>
        public string ModelEndpointId { get; set; } = String.Empty;

        /// <summary>
        /// Result: Healthy or Unhealthy.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// What ran the check: Sweep or Validate.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Latency in milliseconds, or null when the provider did not answer.
        /// </summary>
        public long? LatencyMs { get; set; } = null;

        /// <summary>
        /// Result message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// When the check ran (UTC).
        /// </summary>
        public DateTime CheckedUtc { get; set; } = DateTime.MinValue;

        #endregion
    }
}
