namespace Hugo.Sdk.Enumeration
{
    using System.Collections.Generic;

    /// <summary>
    /// One numbered page (pageNumber, pageSize, totalCount, totalPages, objects), used by audit and request history.
    /// </summary>
    /// <typeparam name="T">Record type.</typeparam>
    public class PageResult<T>
    {
        #region Public-Members

        /// <summary>
        /// Page number, 1-based.
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
        /// Records in this page.
        /// </summary>
        public List<T> Objects { get; set; } = new List<T>();

        #endregion
    }
}
