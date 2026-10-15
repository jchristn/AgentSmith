// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/ForeignKeyDetail.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, referenced schema. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;

    /// <summary>
    /// One column of a declared foreign key. Multi-column keys appear as one entry per column with the same constraint
    /// name. Not thread-safe for mutation.
    /// </summary>
    public class ForeignKeyDetail
    {
        #region Public-Members

        /// <summary>
        /// Constraint name.
        /// </summary>
        public string ConstraintName { get; set; } = String.Empty;

        /// <summary>
        /// Referencing column in this table.
        /// </summary>
        public string ColumnName { get; set; } = String.Empty;

        /// <summary>
        /// Schema of the referenced table, or null when the engine does not report one.
        /// </summary>
        public string? ReferencedSchema { get; set; } = null;

        /// <summary>
        /// Referenced table.
        /// </summary>
        public string ReferencedTable { get; set; } = String.Empty;

        /// <summary>
        /// Referenced column, or null when the key implicitly references the primary key (SQLite).
        /// </summary>
        public string? ReferencedColumn { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ForeignKeyDetail()
        {
        }

        #endregion
    }
}
