// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/IndexDetail.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, primary key flag. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One index of a table. Not thread-safe for mutation.
    /// </summary>
    public class IndexDetail
    {
        #region Public-Members

        /// <summary>
        /// Index name.
        /// </summary>
        public string IndexName { get; set; } = String.Empty;

        /// <summary>
        /// Indexed columns in key order. Expression parts appear as <c>(expression)</c>. Never null.
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
        /// Whether the index enforces uniqueness.
        /// </summary>
        public bool IsUnique { get; set; } = false;

        /// <summary>
        /// Whether the index backs the primary key.
        /// </summary>
        public bool IsPrimary { get; set; } = false;

        #endregion

        #region Private-Members

        private List<string> _Columns = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public IndexDetail()
        {
        }

        #endregion
    }
}
