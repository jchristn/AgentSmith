namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Row-capped result of one SQL execution, serialized as the structured content of SQL tools. Cell values are
    /// rendered as invariant-culture strings (NULL as null). Not thread-safe for mutation.
    /// </summary>
    public class SqlQueryResult
    {
        #region Public-Members

        /// <summary>
        /// Optional section title (for example <c>Locks</c>, <c>Blocking</c>).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Statement type label from the classifier, when known.
        /// </summary>
        public string? StatementType { get; set; } = null;

        /// <summary>
        /// Columns of the first result set. Empty for statements that return no rows. Never null.
        /// </summary>
        public List<SqlColumnInfo> Columns
        {
            get
            {
                return _Columns;
            }
            set
            {
                _Columns = value ?? new List<SqlColumnInfo>();
            }
        }

        /// <summary>
        /// Rows aligned with <see cref="Columns"/>. Never null.
        /// </summary>
        public List<List<string?>> Rows
        {
            get
            {
                return _Rows;
            }
            set
            {
                _Rows = value ?? new List<List<string?>>();
            }
        }

        /// <summary>
        /// Number of rows returned (after the cap).
        /// </summary>
        public int RowCount
        {
            get
            {
                return _Rows.Count;
            }
        }

        /// <summary>
        /// Row cap applied.
        /// </summary>
        public int RowLimit { get; set; } = 0;

        /// <summary>
        /// Whether more rows existed than the cap (or the byte limit) allowed.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Rows inserted, updated, or deleted, when the engine reports it; null otherwise.
        /// </summary>
        public int? RecordsAffected { get; set; } = null;

        /// <summary>
        /// Execution time in milliseconds.
        /// </summary>
        public double ElapsedMs { get; set; } = 0;

        /// <summary>
        /// Optional note for the caller (for example why a tool is not applicable).
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion

        #region Private-Members

        private List<SqlColumnInfo> _Columns = new List<SqlColumnInfo>();
        private List<List<string?>> _Rows = new List<List<string?>>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render the rows as a compact text table with <see cref="TextTable"/>.
        /// </summary>
        /// <param name="maxCellChars">Maximum characters per cell. Default 500.</param>
        /// <returns>Table text, or an empty string when there are no columns.</returns>
        public string RenderTable(int maxCellChars = 500)
        {
            if (_Columns.Count == 0) return String.Empty;
            List<string> names = _Columns.Select(c => c.Name).ToList();
            List<IList<string?>> rows = _Rows.Select(r => (IList<string?>)r).ToList();
            return TextTable.Render(names, rows, maxCellChars);
        }

        /// <summary>
        /// Index of a column by case-insensitive name.
        /// </summary>
        /// <param name="name">Column name.</param>
        /// <returns>Index, or -1.</returns>
        public int ColumnIndex(string name)
        {
            for (int i = 0; i < _Columns.Count; i++)
            {
                if (String.Equals(_Columns[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        #endregion
    }
}
