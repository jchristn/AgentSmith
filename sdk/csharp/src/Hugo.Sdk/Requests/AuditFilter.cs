namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Audit record filter and paging.
    /// </summary>
    public class AuditFilter
    {
        #region Public-Members

        /// <summary>
        /// Event type filter.
        /// </summary>
        public string? EventType { get; set; } = null;

        /// <summary>
        /// Principal Identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Authorization result filter.
        /// </summary>
        public string? Result { get; set; } = null;

        /// <summary>
        /// From UTC.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// To UTC.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Page number, 1-based.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size (server default 25).
        /// </summary>
        public int? PageSize { get; set; } = null;

        #endregion
    }
}
