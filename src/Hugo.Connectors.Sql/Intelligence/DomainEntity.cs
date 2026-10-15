// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/DomainEntity.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, table type and row estimate, context source instead of the
// Tablix table ID. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The domain reading of one table: its likely role and the columns that matter for reasoning. Not thread-safe for
    /// mutation.
    /// </summary>
    public class DomainEntity
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
        /// Object type (for example <c>BASE TABLE</c> or <c>VIEW</c>).
        /// </summary>
        public string TableType { get; set; } = "BASE TABLE";

        /// <summary>
        /// Estimated rows, or null when unknown.
        /// </summary>
        public long? RowEstimate { get; set; } = null;

        /// <summary>
        /// Inferred role: <c>core entity</c>, <c>entity</c>, <c>event</c>, <c>lookup</c>, <c>junction</c>, or
        /// <c>view</c>.
        /// </summary>
        public string Role { get; set; } = "entity";

        /// <summary>
        /// Short explanation of the table's shape.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Important columns for reasoning (keys, filters, metrics). Never null.
        /// </summary>
        public List<string> KeyColumns
        {
            get
            {
                return _KeyColumns;
            }
            set
            {
                _KeyColumns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Whether durable table context exists.
        /// </summary>
        public bool HasContext { get; set; } = false;

        /// <summary>
        /// Who produced the table context (<c>Operator</c>, <c>Generated</c>, <c>Agent</c>, <c>Derived</c>), or null.
        /// </summary>
        public string? ContextSource { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _KeyColumns = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DomainEntity()
        {
        }

        #endregion
    }
}
