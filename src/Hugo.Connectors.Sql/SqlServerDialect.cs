namespace Hugo.Connectors.Sql
{
    using Microsoft.Data.SqlClient;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// SQL Server dialect. SQL Server has no session-level read-only mode, so read-only access relies on the statement
    /// classifier; operators should bind a login that is only a member of <c>db_datareader</c>. Plans come from
    /// <c>SET SHOWPLAN_TEXT ON</c>, which compiles without executing. Stateless and thread-safe.
    /// </summary>
    public class SqlServerDialect : SqlDialect
    {
        #region Public-Members

        /// <inheritdoc />
        public override SqlEngineEnum Engine
        {
            get
            {
                return SqlEngineEnum.SqlServer;
            }
        }

        /// <inheritdoc />
        public override string DisplayName
        {
            get
            {
                return "SQL Server";
            }
        }

        /// <inheritdoc />
        public override ISchemaCrawler Crawler
        {
            get
            {
                return _Crawler;
            }
        }

        /// <inheritdoc />
        public override bool SupportsSessionReadOnly
        {
            get
            {
                return false;
            }
        }

        /// <inheritdoc />
        public override string VersionSql
        {
            get
            {
                return "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) + N' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128))";
            }
        }

        /// <inheritdoc />
        public override string ListSchemasSql
        {
            get
            {
                return "SELECT s.name AS schema_name, p.name AS owner FROM sys.schemas s "
                    + "JOIN sys.database_principals p ON p.principal_id = s.principal_id "
                    + "WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA', 'guest') AND s.name NOT LIKE 'db[_]%' ORDER BY s.name";
            }
        }

        #endregion

        #region Private-Members

        private readonly SqlServerSchemaCrawler _Crawler = new SqlServerSchemaCrawler();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string BuildConnectionString(SqlResourceOptions options, bool readOnly)
        {
            ArgumentNullException.ThrowIfNull(options);
            SqlConnectionStringBuilder builder;
            if (!String.IsNullOrEmpty(options.ConnectionString))
            {
                builder = new SqlConnectionStringBuilder(options.ConnectionString);
            }
            else
            {
                if (String.IsNullOrEmpty(options.Host)) throw new ConnectorException("setting_missing", "Required setting 'host' is not set.");
                builder = new SqlConnectionStringBuilder();
                builder.DataSource = options.Host + "," + (options.Port ?? 1433).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!String.IsNullOrEmpty(options.Database)) builder.InitialCatalog = options.Database;
                if (!String.IsNullOrEmpty(options.Username)) builder.UserID = options.Username;
                if (!String.IsNullOrEmpty(options.Password)) builder.Password = options.Password;
                builder.ConnectTimeout = options.ConnectTimeoutSeconds;
                builder.TrustServerCertificate = options.TrustServerCertificate;
                switch (NormalizeSslMode(options.SslMode))
                {
                    case "Disable":
                    case "Prefer":
                        builder.Encrypt = SqlConnectionEncryptOption.Optional;
                        break;
                    case "VerifyCA":
                    case "VerifyFull":
                        builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
                        builder.TrustServerCertificate = false;
                        break;
                    default:
                        builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
                        break;
                }
            }
            if (String.IsNullOrEmpty(builder.ApplicationName) || builder.ApplicationName == "Framework Microsoft SqlClient Data Provider" || builder.ApplicationName == "Core Microsoft SqlClient Data Provider")
                builder.ApplicationName = options.ApplicationName;
            builder.CommandTimeout = options.CommandTimeoutSeconds;
            return builder.ToString();
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(SqlResourceOptions options, bool readOnly)
        {
            return new SqlConnection(BuildConnectionString(options, readOnly));
        }

        /// <inheritdoc />
        public override string DefaultSchema(SqlResourceOptions options, DbConnection connection)
        {
            ArgumentNullException.ThrowIfNull(options);
            return String.IsNullOrEmpty(options.Schema) ? "dbo" : options.Schema;
        }

        /// <inheritdoc />
        public override async Task<SqlQueryResult> ExplainAsync(SqlSession session, string sql, bool analyze, IList<SqlParameterValue>? parameters, int maxRows, int timeoutSeconds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            using (DbCommand on = session.CreateCommand("SET SHOWPLAN_TEXT ON", timeoutSeconds))
            {
                await on.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            try
            {
                return await SqlExecutor.ExecuteAsync(session, sql, parameters, maxRows, timeoutSeconds, token).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    using (DbCommand off = session.CreateCommand("SET SHOWPLAN_TEXT OFF", timeoutSeconds))
                    {
                        await off.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception e) when (e is DbException || e is InvalidOperationException)
                {
                    if (session.Connection is SqlConnection pooled) SqlConnection.ClearPool(pooled);
                }
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string ExplainPrefix(bool analyze)
        {
            return String.Empty;
        }

        /// <inheritdoc />
        protected override string? ActivitySql(bool includeIdle)
        {
            return "SELECT s.session_id, r.blocking_session_id AS blocked_by, s.login_name, s.host_name, s.program_name, "
                + "COALESCE(r.status, s.status) AS status, r.command, r.wait_type, r.wait_time AS wait_ms, "
                + "r.total_elapsed_time AS elapsed_ms, DB_NAME(COALESCE(r.database_id, s.database_id)) AS database_name, "
                + "LEFT(t.text, 300) AS query "
                + "FROM sys.dm_exec_sessions s "
                + "LEFT JOIN sys.dm_exec_requests r ON r.session_id = s.session_id "
                + "OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t "
                + "WHERE s.is_user_process = 1 AND s.session_id <> @@SPID "
                + (includeIdle ? String.Empty : "AND r.session_id IS NOT NULL ")
                + "ORDER BY r.total_elapsed_time DESC, s.session_id";
        }

        /// <inheritdoc />
        protected override string? TableSizesSql()
        {
            return "SELECT t.name AS table_name, SUM(CASE WHEN p.index_id IN (0, 1) THEN p.row_count ELSE 0 END) AS row_count, "
                + "SUM(p.reserved_page_count) * 8192 AS total_bytes, SUM(p.used_page_count) * 8192 AS used_bytes "
                + "FROM sys.dm_db_partition_stats p "
                + "JOIN sys.tables t ON t.object_id = p.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id "
                + "WHERE s.name = @schema GROUP BY t.name ORDER BY total_bytes DESC, t.name";
        }

        /// <inheritdoc />
        protected override string? LocksSql()
        {
            return "SELECT l.request_session_id AS session_id, l.resource_type, DB_NAME(l.resource_database_id) AS database_name, "
                + "CASE WHEN l.resource_type = 'OBJECT' THEN OBJECT_NAME(l.resource_associated_entity_id, l.resource_database_id) END AS object_name, "
                + "l.request_mode, l.request_status, r.blocking_session_id AS blocked_by "
                + "FROM sys.dm_tran_locks l LEFT JOIN sys.dm_exec_requests r ON r.session_id = l.request_session_id "
                + "WHERE l.request_session_id <> @@SPID AND l.resource_type <> 'DATABASE' "
                + "ORDER BY l.request_status DESC, l.request_session_id";
        }

        /// <inheritdoc />
        protected override string? BlockingSql()
        {
            return "SELECT r.session_id AS blocked_session, r.blocking_session_id AS blocking_session, r.wait_type, "
                + "r.wait_time AS wait_ms, LEFT(t.text, 150) AS blocked_query, LEFT(bt.text, 150) AS blocking_query "
                + "FROM sys.dm_exec_requests r "
                + "OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t "
                + "LEFT JOIN sys.dm_exec_connections bc ON bc.session_id = r.blocking_session_id "
                + "OUTER APPLY sys.dm_exec_sql_text(bc.most_recent_sql_handle) bt "
                + "WHERE r.blocking_session_id <> 0 ORDER BY r.wait_time DESC";
        }

        #endregion
    }
}
