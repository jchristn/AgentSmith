namespace Hugo.Sdk.Enumeration
{
    using System;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// Paging, ordering, and simple filtering for enumeration endpoints (query parameters maxResults, skip, ordering,
    /// search, activeOnly, includeBuiltIn).
    /// </summary>
    public class EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Maximum results per page. Minimum 1, maximum 1000, default 25.
        /// </summary>
        public int MaxResults
        {
            get
            {
                return _MaxResults;
            }
            set
            {
                _MaxResults = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Records to skip. Minimum 0, default 0.
        /// </summary>
        public int Skip
        {
            get
            {
                return _Skip;
            }
            set
            {
                _Skip = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Ordering. Default CreatedDescending (newest first).
        /// </summary>
        public EnumerationOrderEnum Ordering { get; set; } = EnumerationOrderEnum.CreatedDescending;

        /// <summary>
        /// Case-insensitive name substring filter for entities with a name. Null for no filter.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Whether to return only active records. Default false.
        /// </summary>
        public bool ActiveOnly { get; set; } = false;

        /// <summary>
        /// Whether built-in (null tenant) records are included for entities that have them. Default true.
        /// </summary>
        public bool IncludeBuiltIn { get; set; } = true;

        #endregion

        #region Private-Members

        private int _MaxResults = 25;
        private int _Skip = 0;

        #endregion
    }
}
