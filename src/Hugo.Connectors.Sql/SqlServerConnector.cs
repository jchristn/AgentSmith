namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// SQL Server connector (type <c>sqlserver</c>, family <c>sql</c>). Thread-safe; one instance serves every resource of
    /// this type.
    /// </summary>
    public class SqlServerConnector : SqlConnectorBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public SqlServerConnector() : base(
            new SqlServerDialect(),
            "sqlserver",
            "Microsoft SQL Server: schema discovery, row-capped queries, SHOWPLAN plans, requests and blocking, table sizes, and locks. Read-only access relies on the classifier and a db_datareader login.",
            "https://learn.microsoft.com/sql/sql-server/")
        {
        }
    }
}
