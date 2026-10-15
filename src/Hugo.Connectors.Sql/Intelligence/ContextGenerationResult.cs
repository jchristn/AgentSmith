namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured result of <c>sql_generate_context</c>. Not thread-safe for mutation.
    /// </summary>
    public class ContextGenerationResult
    {
        #region Public-Members

        /// <summary>
        /// Schema.
        /// </summary>
        public string Schema { get; set; } = String.Empty;

        /// <summary>
        /// Model that generated the content, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Entries generated.
        /// </summary>
        public int Generated { get; set; } = 0;

        /// <summary>
        /// Entries skipped (operator or agent authored without overwrite).
        /// </summary>
        public int Skipped { get; set; } = 0;

        /// <summary>
        /// Entries that failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Tables not processed because of the table cap.
        /// </summary>
        public int Remaining { get; set; } = 0;

        /// <summary>
        /// Per-entry outcomes, database first. Never null.
        /// </summary>
        public List<ContextGenerationItem> Items
        {
            get
            {
                return _Items;
            }
            set
            {
                _Items = value ?? new List<ContextGenerationItem>();
            }
        }

        #endregion

        #region Private-Members

        private List<ContextGenerationItem> _Items = new List<ContextGenerationItem>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ContextGenerationResult()
        {
        }

        #endregion
    }
}
