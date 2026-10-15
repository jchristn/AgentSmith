namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A numeric column that likely measures something, with its natural aggregations. Hugo addition to the Tablix
    /// metric labels. Not thread-safe for mutation.
    /// </summary>
    public class MetricCandidate
    {
        #region Public-Members

        /// <summary>
        /// Qualified column (<c>schema.table.column</c>).
        /// </summary>
        public string Column { get; set; } = String.Empty;

        /// <summary>
        /// Data type as crawled.
        /// </summary>
        public string DataType { get; set; } = String.Empty;

        /// <summary>
        /// Natural aggregations (for example <c>SUM</c>, <c>AVG</c>). Never null.
        /// </summary>
        public List<string> Aggregations
        {
            get
            {
                return _Aggregations;
            }
            set
            {
                _Aggregations = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Caveat for using the metric, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Aggregations = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MetricCandidate()
        {
        }

        #endregion
    }
}
