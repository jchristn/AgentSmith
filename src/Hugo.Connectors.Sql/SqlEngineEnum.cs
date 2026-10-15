namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// SQL database engine. Determines the driver, the catalog queries, and the tokenizer dialect.
    /// </summary>
    public enum SqlEngineEnum
    {
        /// <summary>
        /// SQLite through Microsoft.Data.Sqlite.
        /// </summary>
        Sqlite = 0,

        /// <summary>
        /// MySQL (and compatible servers) through MySqlConnector.
        /// </summary>
        Mysql = 1,

        /// <summary>
        /// Microsoft SQL Server through Microsoft.Data.SqlClient.
        /// </summary>
        SqlServer = 2,

        /// <summary>
        /// PostgreSQL through Npgsql.
        /// </summary>
        Postgresql = 3
    }
}
