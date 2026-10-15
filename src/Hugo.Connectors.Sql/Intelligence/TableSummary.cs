// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/TableSummary.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, table type, row estimate, key summary, and context marker
// instead of the Tablix table ID. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One line of the compact table index. Not thread-safe for mutation.
    /// </summary>
    public class TableSummary
    {
        #region Public-Members

        /// <summary>
        /// Schema name.
        /// </summary>
        public string SchemaName { get; set; } = String.Empty;

        /// <summary>
        /// Table or view name.
        /// </summary>
        public string TableName { get; set; } = String.Empty;

        /// <summary>
        /// Object type (for example <c>BASE TABLE</c>, <c>VIEW</c>).
        /// </summary>
        public string TableType { get; set; } = "BASE TABLE";

        /// <summary>
        /// Estimated rows, or null when unknown.
        /// </summary>
        public long? RowEstimate { get; set; } = null;

        /// <summary>
        /// Number of columns.
        /// </summary>
        public int Columns { get; set; } = 0;

        /// <summary>
        /// Number of declared foreign key columns.
        /// </summary>
        public int ForeignKeys { get; set; } = 0;

        /// <summary>
        /// Number of indexes.
        /// </summary>
        public int Indexes { get; set; } = 0;

        /// <summary>
        /// Primary key columns. Never null.
        /// </summary>
        public List<string> PrimaryKey
        {
            get
            {
                return _PrimaryKey;
            }
            set
            {
                _PrimaryKey = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Compact key summary (for example <c>PK order_id; FK customer_id-&gt;customers</c>).
        /// </summary>
        public string KeySummary { get; set; } = String.Empty;

        /// <summary>
        /// Whether durable table context exists.
        /// </summary>
        public bool HasContext { get; set; } = false;

        /// <summary>
        /// Who produced the table context, or null.
        /// </summary>
        public string? ContextSource { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _PrimaryKey = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TableSummary()
        {
        }

        #endregion
    }
}
