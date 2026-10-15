namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Typed filter for audit queries.
    /// </summary>
    public class AuditFilter
    {
        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Event type.
        /// </summary>
        public AuditEventTypeEnum? EventType { get; set; } = null;

        /// <summary>
        /// Principal identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Authorization result.
        /// </summary>
        public AuthorizationResultEnum? Result { get; set; } = null;

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
    }
}
