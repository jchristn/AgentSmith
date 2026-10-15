namespace Hugo.Connectors.Sql.Intelligence
{
    using System;

    /// <summary>
    /// One interpretation of an ambiguous term: a table, or a column of a table. Immutable and thread-safe.
    /// </summary>
    public class AmbiguityCandidate
    {
        #region Public-Members

        /// <summary>
        /// Schema name.
        /// </summary>
        public string Schema { get; }

        /// <summary>
        /// Table name.
        /// </summary>
        public string Table { get; }

        /// <summary>
        /// Column name, or null when the candidate is a whole table.
        /// </summary>
        public string? Column { get; }

        /// <summary>
        /// Qualified label (<c>schema.table</c> or <c>schema.table.column</c>).
        /// </summary>
        public string Label
        {
            get
            {
                string table = TableIdentity.Key(Schema, Table);
                return Column == null ? table : table + "." + Column;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="schema">Schema name.</param>
        /// <param name="table">Table name.</param>
        /// <param name="column">Column name, or null.</param>
        public AmbiguityCandidate(string? schema, string? table, string? column)
        {
            Schema = schema ?? String.Empty;
            Table = table ?? String.Empty;
            Column = column;
        }

        #endregion
    }
}
