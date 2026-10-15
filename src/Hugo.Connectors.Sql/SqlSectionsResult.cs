namespace Hugo.Connectors.Sql
{
    using System.Collections.Generic;

    /// <summary>
    /// Structured content of tools that return several titled result sets (for example <c>sql_locks</c>). Not
    /// thread-safe for mutation.
    /// </summary>
    public class SqlSectionsResult
    {
        #region Public-Members

        /// <summary>
        /// Sections in display order. Never null.
        /// </summary>
        public List<SqlQueryResult> Sections
        {
            get
            {
                return _Sections;
            }
            set
            {
                _Sections = value ?? new List<SqlQueryResult>();
            }
        }

        #endregion

        #region Private-Members

        private List<SqlQueryResult> _Sections = new List<SqlQueryResult>();

        #endregion
    }
}
