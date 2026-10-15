namespace Hugo.Sdk.Enumeration
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One page of an enumeration (maxResults, skip, objects).
    /// </summary>
    /// <typeparam name="T">Record type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// Whether the enumeration succeeded.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Maximum results requested.
        /// </summary>
        public int MaxResults { get; set; } = 0;

        /// <summary>
        /// Records skipped.
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Total matching records.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Records remaining after this page.
        /// </summary>
        public long RecordsRemaining { get; set; } = 0;

        /// <summary>
        /// Whether this page is the last one.
        /// </summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// Server timestamp, UTC.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Records in this page.
        /// </summary>
        public List<T> Objects { get; set; } = new List<T>();

        #endregion
    }
}
