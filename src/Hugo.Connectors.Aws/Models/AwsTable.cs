namespace Hugo.Connectors.Aws.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Rows collected by a tool before rendering: columns, string cells, paging, and notes. Not thread-safe.
    /// </summary>
    public class AwsTable
    {
        #region Public-Members

        /// <summary>
        /// Heading line shown above the table, for example <c>EC2 instances</c>.
        /// </summary>
        public string Heading { get; set; } = String.Empty;

        /// <summary>
        /// Column names.
        /// </summary>
        public List<string> Columns
        {
            get
            {
                return _Columns;
            }
            set
            {
                _Columns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Rows; each row has one cell per column (null renders as NULL).
        /// </summary>
        public List<IList<string?>> Rows
        {
            get
            {
                return _Rows;
            }
            set
            {
                _Rows = value ?? new List<IList<string?>>();
            }
        }

        /// <summary>
        /// Next page token, or null.
        /// </summary>
        public string? NextToken { get; set; } = null;

        /// <summary>
        /// Name of the argument that takes <see cref="NextToken"/>. Default <c>nextToken</c>.
        /// </summary>
        public string NextTokenArgument { get; set; } = "nextToken";

        /// <summary>
        /// Whether rows were dropped before rendering (for example by a client-side cap).
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Scalar values for the structured result.
        /// </summary>
        public Dictionary<string, string?> Details { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// Notes shown after the table.
        /// </summary>
        public List<string> Notes { get; set; } = new List<string>();

        /// <summary>
        /// Text appended after the table and notes (for example a log tail), already bounded by the tool.
        /// </summary>
        public string? Trailer { get; set; } = null;

        /// <summary>
        /// When true, rows appear only in the structured result and the text shows <see cref="Trailer"/> instead of a
        /// table (used for log lines, which read better unformatted). Default false.
        /// </summary>
        public bool RowsInTrailerOnly { get; set; } = false;

        #endregion

        #region Private-Members

        private List<string> _Columns = new List<string>();
        private List<IList<string?>> _Rows = new List<IList<string?>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsTable()
        {
        }

        /// <summary>
        /// Instantiate with a heading and columns.
        /// </summary>
        /// <param name="heading">Heading.</param>
        /// <param name="columns">Columns.</param>
        public AwsTable(string heading, params string[] columns)
        {
            Heading = heading ?? String.Empty;
            _Columns = columns == null ? new List<string>() : new List<string>(columns);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a row.
        /// </summary>
        /// <param name="cells">Cells, one per column.</param>
        /// <returns>This table.</returns>
        public AwsTable Add(params string?[] cells)
        {
            _Rows.Add(cells ?? new string?[0]);
            return this;
        }

        #endregion
    }
}
