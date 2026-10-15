namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// SQLite connector (type <c>sqlite</c>, family <c>sql</c>). Thread-safe; one instance serves every resource of
    /// this type.
    /// </summary>
    public class SqliteConnector : SqlConnectorBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public SqliteConnector() : base(
            new SqliteDialect(),
            "sqlite",
            "SQLite database files: schema discovery, row-capped queries, and query plans. Read-only access opens the file with Mode=ReadOnly.",
            "https://www.sqlite.org/docs.html")
        {
        }
    }
}
