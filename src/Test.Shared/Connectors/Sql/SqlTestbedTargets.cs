namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Sql;

    /// <summary>
    /// Connection details and dialect SQL for the docker/testbed/sql targets. Host, ports, and passwords can be
    /// overridden with <c>HUGO_TESTBED_HOST</c>, <c>HUGO_TESTBED_PG_PORT</c>, <c>HUGO_TESTBED_MYSQL_PORT</c>,
    /// <c>HUGO_TESTBED_MSSQL_PORT</c>, <c>HUGO_TESTBED_SQL_PASSWORD</c>, and <c>HUGO_TESTBED_MSSQL_PASSWORD</c>.
    /// </summary>
    public static class SqlTestbedTargets
    {
        #region Public-Methods

        /// <summary>
        /// PostgreSQL 16 on 127.0.0.1:28432.
        /// </summary>
        /// <returns>Target.</returns>
        public static SqlEngineTarget Postgresql()
        {
            return new SqlEngineTarget
            {
                Type = "postgresql",
                Factory = () => new PostgresqlConnector(),
                Settings = () => Server(Env("HUGO_TESTBED_PG_PORT", "28432"), "hugo"),
                Secrets = Password(Env("HUGO_TESTBED_SQL_PASSWORD", "HugoTestbed1!")),
                DefaultSchema = "public",
                RowCapSql = "SELECT g AS n FROM generate_series(1, 1000) AS g",
                SlowSql = "SELECT pg_sleep(10)",
                ShortSleepSql = "SELECT pg_sleep(4)",
                SleepMarker = "pg_sleep",
                HasSessions = true,
                SupportsAnalyze = true,
                EngineReadOnly = true,
                PlanMarker = "orders"
            };
        }

        /// <summary>
        /// MySQL 8.4 on 127.0.0.1:28306.
        /// </summary>
        /// <returns>Target.</returns>
        public static SqlEngineTarget Mysql()
        {
            return new SqlEngineTarget
            {
                Type = "mysql",
                Factory = () => new MysqlConnector(),
                Settings = () => Server(Env("HUGO_TESTBED_MYSQL_PORT", "28306"), "hugo"),
                Secrets = Password(Env("HUGO_TESTBED_SQL_PASSWORD", "HugoTestbed1!")),
                DefaultSchema = "shop",
                RowCapSql = "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1000) SELECT i FROM n",
                SlowSql = "SELECT SLEEP(10)",
                ShortSleepSql = "SELECT SLEEP(4)",
                SleepMarker = "SLEEP",
                HasSessions = true,
                SupportsAnalyze = true,
                EngineReadOnly = true,
                PlanMarker = "orders"
            };
        }

        /// <summary>
        /// SQL Server 2022 on 127.0.0.1:28433.
        /// </summary>
        /// <returns>Target.</returns>
        public static SqlEngineTarget SqlServer()
        {
            return new SqlEngineTarget
            {
                Type = "sqlserver",
                Factory = () => new SqlServerConnector(),
                Settings = () =>
                {
                    Dictionary<string, string> s = Server(Env("HUGO_TESTBED_MSSQL_PORT", "28433"), "sa");
                    s["trustServerCertificate"] = "true";
                    return s;
                },
                Secrets = Password(Env("HUGO_TESTBED_MSSQL_PASSWORD", "Hugo!Testbed2026")),
                DefaultSchema = "dbo",
                RowCapSql = "WITH n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1000) SELECT i FROM n OPTION (MAXRECURSION 1000)",
                SlowSql = "WAITFOR DELAY '00:00:10'",
                ShortSleepSql = "WAITFOR DELAY '00:00:04'",
                SleepMarker = "WAITFOR",
                HasSessions = true,
                SupportsAnalyze = false,
                EngineReadOnly = false,
                PlanMarker = "orders"
            };
        }

        /// <summary>
        /// A temporary SQLite file seeded at first use.
        /// </summary>
        /// <param name="file">Lazily created file; the caller disposes it.</param>
        /// <returns>Target.</returns>
        public static SqlEngineTarget Sqlite(Lazy<SqliteShopFile> file)
        {
            ArgumentNullException.ThrowIfNull(file);
            return new SqlEngineTarget
            {
                Type = "sqlite",
                Factory = () => new SqliteConnector(),
                Settings = () => new Dictionary<string, string> { { "filename", file.Value.Path } },
                DefaultSchema = "main",
                RowCapSql = "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1000) SELECT i FROM n",
                SlowSql = "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n) SELECT count(*) FROM n",
                ShortSleepSql = String.Empty,
                SleepMarker = String.Empty,
                HasSessions = false,
                SupportsAnalyze = false,
                EngineReadOnly = true,
                PlanMarker = "orders"
            };
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> Server(string port, string username)
        {
            return new Dictionary<string, string>
            {
                { "host", Env("HUGO_TESTBED_HOST", "127.0.0.1") },
                { "port", port },
                { "database", "shop" },
                { "username", username },
                { "connectTimeoutSeconds", "10" }
            };
        }

        private static Dictionary<string, Dictionary<string, string>> Password(string password)
        {
            return new Dictionary<string, Dictionary<string, string>>
            {
                { "password", new Dictionary<string, string> { { "password", password } } }
            };
        }

        private static string Env(string name, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrEmpty(value) ? fallback : value;
        }

        #endregion
    }
}
