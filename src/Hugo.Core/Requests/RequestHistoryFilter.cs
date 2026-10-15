namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Typed filter for request history queries.
    /// </summary>
    public class RequestHistoryFilter
    {
        /// <summary>
        /// Tenant identifier. Null means all tenants (system administrators only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Exact status code.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Case-insensitive path substring.
        /// </summary>
        public string? PathContains { get; set; } = null;

        /// <summary>
        /// Lower bound (UTC, inclusive).
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Upper bound (UTC, exclusive).
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Page number, starting at 1.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size. Minimum 1, maximum 1000.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Summary bucket size in minutes. Minimum 1, maximum 10080.
        /// </summary>
        public int BucketMinutes { get; set; } = 15;
    }
}
