namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Page of request history entries (bodies omitted).
    /// </summary>
    public class RequestHistoryPage
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
        /// Total matching entries.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Total pages.
        /// </summary>
        public int TotalPages { get; set; } = 0;

        /// <summary>
        /// Entries.
        /// </summary>
        public List<Hugo.Core.Models.RequestHistoryEntry> Objects { get; set; } = new List<Hugo.Core.Models.RequestHistoryEntry>();
    }
}
