namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// PostgreSQL connector (type <c>postgresql</c>, family <c>sql</c>). Thread-safe; one instance serves every resource of
    /// this type.
    /// </summary>
    public class PostgresqlConnector : SqlConnectorBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public PostgresqlConnector() : base(
            new PostgresqlDialect(),
            "postgresql",
            "PostgreSQL servers: schema discovery, row-capped queries, EXPLAIN, pg_stat_activity, table sizes, and pg_locks. Reads run in a READ ONLY transaction.",
            "https://www.postgresql.org/docs/")
        {
        }
    }
}
