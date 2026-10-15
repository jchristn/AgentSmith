namespace Hugo.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A page of results.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// Whether the enumeration succeeded.
        /// </summary>
        [JsonPropertyOrder(0)]
        public bool Success { get; set; } = true;

        /// <summary>
        /// Requested page size.
        /// </summary>
        [JsonPropertyOrder(1)]
        public int MaxResults { get; set; } = 25;

        /// <summary>
        /// Records skipped.
        /// </summary>
        [JsonPropertyOrder(2)]
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Total matching records.
        /// </summary>
        [JsonPropertyOrder(3)]
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Records remaining after this page.
        /// </summary>
        [JsonPropertyOrder(4)]
        public long RecordsRemaining { get; set; } = 0;

        /// <summary>
        /// Whether this is the last page.
        /// </summary>
        [JsonPropertyOrder(5)]
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// Server timestamp (UTC).
        /// </summary>
        [JsonPropertyOrder(6)]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Records in this page.
        /// </summary>
        [JsonPropertyOrder(999)]
        public List<T> Objects
        {
            get
            {
                return _Objects;
            }
            set
            {
                _Objects = value ?? new List<T>();
            }
        }

        #endregion

        #region Private-Members

        private List<T> _Objects = new List<T>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a page.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="totalRecords">Total matching records.</param>
        /// <param name="objects">Page contents.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        public static EnumerationResult<T> Build(EnumerationQuery query, long totalRecords, List<T> objects)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<T> page = objects ?? new List<T>();
            long remaining = totalRecords - ((long)query.Skip + page.Count);
            if (remaining < 0) remaining = 0;

            EnumerationResult<T> result = new EnumerationResult<T>();
            result.MaxResults = query.MaxResults;
            result.Skip = query.Skip;
            result.TotalRecords = totalRecords;
            result.RecordsRemaining = remaining;
            result.EndOfResults = remaining <= 0;
            result.Objects = page;
            return result;
        }

        #endregion
    }
}
