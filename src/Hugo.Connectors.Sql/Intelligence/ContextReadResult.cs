namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Structured result of <c>sql_get_context</c>: the database entry and the requested or paged table entries.
    /// Not thread-safe for mutation.
    /// </summary>
    public class ContextReadResult
    {
        #region Public-Members

        /// <summary>
        /// Schema the table keys refer to.
        /// </summary>
        public string Schema { get; set; } = String.Empty;

        /// <summary>
        /// Database-level entry, or null when none is saved.
        /// </summary>
        public ResourceKnowledgeEntry? Database { get; set; } = null;

        /// <summary>
        /// Table entries. Never null.
        /// </summary>
        public List<ResourceKnowledgeEntry> Tables
        {
            get
            {
                return _Tables;
            }
            set
            {
                _Tables = value ?? new List<ResourceKnowledgeEntry>();
            }
        }

        /// <summary>
        /// Tables on this page that have no context yet (when listing with includeEmpty). Never null.
        /// </summary>
        public List<string> TablesWithoutContext
        {
            get
            {
                return _TablesWithoutContext;
            }
            set
            {
                _TablesWithoutContext = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Table entries (or tables, with includeEmpty) matching across all pages.
        /// </summary>
        public int TotalRecords { get; set; } = 0;

        /// <summary>
        /// Cursor for the next page, or null.
        /// </summary>
        public string? NextCursor { get; set; } = null;

        #endregion

        #region Private-Members

        private List<ResourceKnowledgeEntry> _Tables = new List<ResourceKnowledgeEntry>();
        private List<string> _TablesWithoutContext = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ContextReadResult()
        {
        }

        #endregion
    }
}
