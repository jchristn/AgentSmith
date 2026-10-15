namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Sql;

    /// <summary>
    /// A database the SQL tool suites run against, with the dialect-specific SQL and expectations they need.
    /// Not thread-safe for mutation.
    /// </summary>
    public class SqlEngineTarget
    {
        /// <summary>
        /// Connector type (for example <c>postgresql</c>).
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Creates a fresh connector.
        /// </summary>
        public Func<SqlConnectorBase> Factory { get; set; } = () => new SqliteConnector();

        /// <summary>
        /// Produces resource settings (evaluated per test so file-based targets can be created lazily).
        /// </summary>
        public Func<Dictionary<string, string>> Settings { get; set; } = () => new Dictionary<string, string>();

        /// <summary>
        /// Secrets by slot then field. Never null.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Secrets { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>
        /// Expected default schema.
        /// </summary>
        public string DefaultSchema { get; set; } = String.Empty;

        /// <summary>
        /// Query returning the integers 1 to 1000, one per row.
        /// </summary>
        public string RowCapSql { get; set; } = String.Empty;

        /// <summary>
        /// Statement that runs for about ten seconds (or forever) without writing.
        /// </summary>
        public string SlowSql { get; set; } = String.Empty;

        /// <summary>
        /// Statement that runs for about four seconds, used to observe activity.
        /// </summary>
        public string ShortSleepSql { get; set; } = String.Empty;

        /// <summary>
        /// Text that identifies <see cref="ShortSleepSql"/> in activity output.
        /// </summary>
        public string SleepMarker { get; set; } = String.Empty;

        /// <summary>
        /// Whether the engine has sessions to show in sql_activity and sql_locks.
        /// </summary>
        public bool HasSessions { get; set; } = true;

        /// <summary>
        /// Whether sql_explain supports analyze.
        /// </summary>
        public bool SupportsAnalyze { get; set; } = false;

        /// <summary>
        /// Whether the engine enforces read-only sessions.
        /// </summary>
        public bool EngineReadOnly { get; set; } = true;

        /// <summary>
        /// Text expected somewhere in an estimated plan for a filtered orders query.
        /// </summary>
        public string PlanMarker { get; set; } = String.Empty;
    }
}
