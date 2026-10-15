// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/DatabaseDetail.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: renamed to SchemaDetail, namespace, engine name as a string, removed Tablix-only fields
// (DatabaseId, Context, IsCrawled). See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The result of crawling one schema. Not thread-safe for mutation.
    /// </summary>
    public class SchemaDetail
    {
        #region Public-Members

        /// <summary>
        /// Engine name (for example <c>postgresql</c>).
        /// </summary>
        public string Engine { get; set; } = String.Empty;

        /// <summary>
        /// Database name, or file name for SQLite.
        /// </summary>
        public string? DatabaseName { get; set; } = null;

        /// <summary>
        /// Schema that was crawled.
        /// </summary>
        public string Schema { get; set; } = String.Empty;

        /// <summary>
        /// Tables and views. Never null.
        /// </summary>
        public List<TableDetail> Tables
        {
            get
            {
                return _Tables;
            }
            set
            {
                _Tables = value ?? new List<TableDetail>();
            }
        }

        /// <summary>
        /// UTC time the crawl finished.
        /// </summary>
        public DateTime CrawledUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<TableDetail> _Tables = new List<TableDetail>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SchemaDetail()
        {
        }

        #endregion
    }
}
