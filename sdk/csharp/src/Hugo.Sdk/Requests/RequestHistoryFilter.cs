namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Request history filter and paging.
    /// </summary>
    public class RequestHistoryFilter
    {
        #region Public-Members

        /// <summary>
        /// HTTP method filter.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Status code.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Path substring filter.
        /// </summary>
        public string? PathContains { get; set; } = null;

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

        /// <summary>
        /// Tenant filter (system administrators).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User Identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        #endregion
    }
}
