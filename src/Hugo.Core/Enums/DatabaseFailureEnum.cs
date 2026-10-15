namespace Hugo.Core.Enums
{
    /// <summary>
    /// How a database exception should be reported to a caller.
    /// </summary>
    public enum DatabaseFailureEnum
    {
        /// <summary>
        /// Not a database availability problem (a query error, a constraint violation, or not a database exception).
        /// </summary>
        None,

        /// <summary>
        /// The database is reachable but has no capacity right now: the connection pool is exhausted, PostgreSQL has no
        /// connection slots left, or Sqlite is busy or locked. Retrying after a short wait usually succeeds.
        /// </summary>
        Busy,

        /// <summary>
        /// The database cannot be reached or is shutting down or restarting (connection refused or reset, network
        /// failure, PostgreSQL cannot accept connections, Sqlite cannot open the file).
        /// </summary>
        Unavailable
    }
}
