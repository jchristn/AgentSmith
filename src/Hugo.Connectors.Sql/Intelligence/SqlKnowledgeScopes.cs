namespace Hugo.Connectors.Sql.Intelligence
{
    /// <summary>
    /// Knowledge scopes the SQL connectors use in <c>IResourceKnowledgeStore</c>. These are part of the stored data
    /// format shared with the dashboard, so they are fixed. Thread-safe.
    /// </summary>
    public static class SqlKnowledgeScopes
    {
        /// <summary>
        /// Database-level context. The key is the empty string.
        /// </summary>
        public const string Database = "database";

        /// <summary>
        /// Table-level context. The key is <c>schema.table</c> (see <see cref="TableIdentity.Key(string, string)"/>).
        /// </summary>
        public const string Table = "table";
    }
}
