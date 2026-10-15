namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// One event published on the in-process event hub and streamed to GET /v1.0/api/events.
    /// </summary>
    public class HubEvent
    {
        #region Public-Members

        /// <summary>
        /// Monotonic sequence number within this process (the SSE event id).
        /// </summary>
        public long Sequence { get; set; } = 0;

        /// <summary>
        /// Event type, for example secret.created, settings.changed, or masterkey.rotation.
        /// </summary>
        public string Type
        {
            get
            {
                return _Type;
            }
            set
            {
                _Type = String.IsNullOrWhiteSpace(value) ? "event" : value;
            }
        }

        /// <summary>
        /// Tenant the event belongs to, or null for a system event (delivered to system administrators only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// JSON payload.
        /// </summary>
        public string Data
        {
            get
            {
                return _Data;
            }
            set
            {
                _Data = value ?? "{}";
            }
        }

        /// <summary>
        /// When the event was published (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Type = "event";
        private string _Data = "{}";

        #endregion
    }
}
