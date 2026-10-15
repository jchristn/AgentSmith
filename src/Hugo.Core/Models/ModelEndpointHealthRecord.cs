namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// One recorded health check of a model endpoint (model_endpoint_health). The table is bounded: only the newest
    /// records per endpoint are kept. Not thread-safe.
    /// </summary>
    public class ModelEndpointHealthRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix mhc_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewModelHealthCheckId() : value;
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
        /// Model endpoint identifier.
        /// </summary>
        public string ModelEndpointId
        {
            get
            {
                return _ModelEndpointId;
            }
            set
            {
                _ModelEndpointId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Result of the check: Healthy or Unhealthy.
        /// </summary>
        public ModelHealthStatusEnum Status { get; set; } = ModelHealthStatusEnum.Unknown;

        /// <summary>
        /// What ran the check (Sweep or Validate).
        /// </summary>
        public ModelHealthSourceEnum Source { get; set; } = ModelHealthSourceEnum.Sweep;

        /// <summary>
        /// Round-trip latency in milliseconds, or null when the provider did not answer.
        /// </summary>
        public long? LatencyMs { get; set; } = null;

        /// <summary>
        /// Scrubbed result message (OK with latency, or the provider's error), at most 2000 characters.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// When the check ran (UTC).
        /// </summary>
        public DateTime CheckedUtc
        {
            get
            {
                return _CheckedUtc;
            }
            set
            {
                _CheckedUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewModelHealthCheckId();
        private string _TenantId = String.Empty;
        private string _ModelEndpointId = String.Empty;
        private DateTime _CheckedUtc = DateTime.UtcNow;

        #endregion
    }
}
