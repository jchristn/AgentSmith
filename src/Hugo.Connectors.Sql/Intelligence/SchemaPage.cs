// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/EnumerationResult.cs, DatabaseTableListResult.cs, and DatabaseRelationshipListResult.cs.
// Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License. Modified for Hugo: one generic page with an opaque
// cursor instead of skip and NextSkip, filters recorded on the page. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One page of a compact index (tables or relationships). Not thread-safe for mutation.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public class SchemaPage<T>
    {
        #region Public-Members

        /// <summary>
        /// Schema the index describes.
        /// </summary>
        public string Schema { get; set; } = String.Empty;

        /// <summary>
        /// Search text applied, or null.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Table filter applied (relationships only), or null.
        /// </summary>
        public string? Table { get; set; } = null;

        /// <summary>
        /// Objects in the crawl before filtering.
        /// </summary>
        public int TableCount { get; set; } = 0;

        /// <summary>
        /// Items matching the filters across all pages.
        /// </summary>
        public int TotalRecords { get; set; } = 0;

        /// <summary>
        /// Page size applied.
        /// </summary>
        public int PageSize { get; set; } = 0;

        /// <summary>
        /// Cursor this page started at (empty for the first page).
        /// </summary>
        public string Cursor { get; set; } = String.Empty;

        /// <summary>
        /// Cursor for the next page, or null at the end.
        /// </summary>
        public string? NextCursor { get; set; } = null;

        /// <summary>
        /// Whether this is the last page.
        /// </summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// UTC time of the crawl the page was cut from.
        /// </summary>
        public DateTime CrawledUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Items. Never null.
        /// </summary>
        public List<T> Items
        {
            get
            {
                return _Items;
            }
            set
            {
                _Items = value ?? new List<T>();
            }
        }

        #endregion

        #region Private-Members

        private List<T> _Items = new List<T>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SchemaPage()
        {
        }

        #endregion
    }
}
