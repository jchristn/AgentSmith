namespace Test.Shared.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared.Connectors.Sql;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Sql connector family. Unit suites always run; testbed suites run when HUGO_TESTBED=1.
    /// </summary>
    public static class SqlSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            suites.Add(SqlClassifierSuite.Build());
            suites.Add(SqlUnitSuite.Build());

            Lazy<SqliteShopFile> sqliteFile = new Lazy<SqliteShopFile>(() => new SqliteShopFile());
            suites.Add(new TestSuiteDescriptor(
                "connectors.sql.sqlite",
                "SQL connectors against a temporary SQLite file",
                SqlToolSuiteBuilder.Build("connectors.sql.sqlite", SqlTestbedTargets.Sqlite(sqliteFile), false, null),
                null,
                ct =>
                {
                    if (sqliteFile.IsValueCreated) sqliteFile.Value.Dispose();
                    return ValueTask.CompletedTask;
                }));

            bool enabled = SqlTestSupport.TestbedEnabled();
            string? reason = enabled ? null : "Set HUGO_TESTBED=1 and start docker/testbed/sql/compose.yaml to run SQL testbed suites.";

            SqlEngineTarget pg = SqlTestbedTargets.Postgresql();
            List<TestCaseDescriptor> pgCases = SqlToolSuiteBuilder.Build("testbed.sql.postgresql", pg, !enabled, reason);
            pgCases.AddRange(SqlTestbedExtras.Postgresql("testbed.sql.postgresql", pg, !enabled, reason));
            suites.Add(new TestSuiteDescriptor("testbed.sql.postgresql", "SQL connectors against testbed PostgreSQL 16", pgCases));

            SqlEngineTarget mysql = SqlTestbedTargets.Mysql();
            List<TestCaseDescriptor> mysqlCases = SqlToolSuiteBuilder.Build("testbed.sql.mysql", mysql, !enabled, reason);
            mysqlCases.AddRange(SqlTestbedExtras.Mysql("testbed.sql.mysql", mysql, !enabled, reason));
            suites.Add(new TestSuiteDescriptor("testbed.sql.mysql", "SQL connectors against testbed MySQL 8.4", mysqlCases));

            SqlEngineTarget mssql = SqlTestbedTargets.SqlServer();
            suites.Add(new TestSuiteDescriptor("testbed.sql.sqlserver", "SQL connectors against testbed SQL Server 2022", SqlToolSuiteBuilder.Build("testbed.sql.sqlserver", mssql, !enabled, reason)));
            return suites;
        }
    }
}
