// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/TableDetail.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, table type and row estimate, removed Tablix-only fields
// (TableId, Context). See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A table or view with its columns, keys, and indexes. Not thread-safe for mutation.
    /// </summary>
    public class TableDetail
    {
        #region Public-Members

        /// <summary>
        /// Schema name (<c>main</c> for SQLite, the database name for MySQL).
        /// </summary>
        public string SchemaName { get; set; } = String.Empty;

        /// <summary>
        /// Table or view name.
        /// </summary>
        public string TableName { get; set; } = String.Empty;

        /// <summary>
        /// Object type: <c>BASE TABLE</c>, <c>VIEW</c>, <c>MATERIALIZED VIEW</c>, or <c>FOREIGN TABLE</c>.
        /// </summary>
        public string TableType { get; set; } = "BASE TABLE";

        /// <summary>
        /// Estimated row count from catalog statistics (exact for SQLite), or null when unknown.
        /// </summary>
        public long? RowEstimate { get; set; } = null;

        /// <summary>
        /// Columns in ordinal order. Never null.
        /// </summary>
        public List<ColumnDetail> Columns
        {
            get
            {
                return _Columns;
            }
            set
            {
                _Columns = value ?? new List<ColumnDetail>();
            }
        }

        /// <summary>
        /// Foreign keys. Never null.
        /// </summary>
        public List<ForeignKeyDetail> ForeignKeys
        {
            get
            {
                return _ForeignKeys;
            }
            set
            {
                _ForeignKeys = value ?? new List<ForeignKeyDetail>();
            }
        }

        /// <summary>
        /// Indexes. Never null.
        /// </summary>
        public List<IndexDetail> Indexes
        {
            get
            {
                return _Indexes;
            }
            set
            {
                _Indexes = value ?? new List<IndexDetail>();
            }
        }

        #endregion

        #region Private-Members

        private List<ColumnDetail> _Columns = new List<ColumnDetail>();
        private List<ForeignKeyDetail> _ForeignKeys = new List<ForeignKeyDetail>();
        private List<IndexDetail> _Indexes = new List<IndexDetail>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TableDetail()
        {
        }

        #endregion
    }
}
