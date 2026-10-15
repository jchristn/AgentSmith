namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Page of audit records.
    /// </summary>
    public class AuditPage
    {
        /// <summary>
        /// Page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Total matching records.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Total pages.
        /// </summary>
        public int TotalPages { get; set; } = 0;

        /// <summary>
        /// Records.
        /// </summary>
        public List<Hugo.Core.Models.AuditRecord> Objects { get; set; } = new List<Hugo.Core.Models.AuditRecord>();
    }
}
