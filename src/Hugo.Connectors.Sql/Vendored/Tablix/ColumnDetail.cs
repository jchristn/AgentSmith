// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/ColumnDetail.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, ordinal position, 64-bit max length. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;

    /// <summary>
    /// One column of a table or view. Not thread-safe for mutation.
    /// </summary>
    public class ColumnDetail
    {
        #region Public-Members

        /// <summary>
        /// Column name.
        /// </summary>
        public string ColumnName { get; set; } = String.Empty;

        /// <summary>
        /// Engine data type as reported by the catalog (for example <c>integer</c>, <c>varchar(100)</c>).
        /// </summary>
        public string DataType { get; set; } = String.Empty;

        /// <summary>
        /// One-based ordinal position within the table.
        /// </summary>
        public int Ordinal { get; set; } = 0;

        /// <summary>
        /// Whether the column accepts NULL. Default true.
        /// </summary>
        public bool IsNullable { get; set; } = true;

        /// <summary>
        /// Whether the column is part of the primary key.
        /// </summary>
        public bool IsPrimaryKey { get; set; } = false;

        /// <summary>
        /// Default value expression, or null when none.
        /// </summary>
        public string? DefaultValue { get; set; } = null;

        /// <summary>
        /// Maximum character length for character types, or null when not applicable.
        /// </summary>
        public long? MaxLength { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ColumnDetail()
        {
        }

        #endregion
    }
}
