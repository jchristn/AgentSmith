namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// MySQL connector (type <c>mysql</c>, family <c>sql</c>). Thread-safe; one instance serves every resource of
    /// this type.
    /// </summary>
    public class MysqlConnector : SqlConnectorBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public MysqlConnector() : base(
            new MysqlDialect(),
            "mysql",
            "MySQL servers: schema discovery, row-capped queries, plans, processlist, table sizes, and InnoDB locks. Reads run in START TRANSACTION READ ONLY.",
            "https://dev.mysql.com/doc/")
        {
        }
    }
}
