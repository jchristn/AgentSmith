namespace Hugo.Connectors.Sql
{
    using System;
    using System.Data;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;
    using MySqlConnector;

    /// <summary>
    /// MySQL dialect. Read-only access is enforced with <c>START TRANSACTION READ ONLY</c>, rolled back when the call
    /// ends. The schema is the MySQL database. Stateless and thread-safe.
    /// </summary>
    public class MysqlDialect : SqlDialect
    {
        #region Public-Members

        /// <inheritdoc />
        public override SqlEngineEnum Engine
        {
            get
            {
                return SqlEngineEnum.Mysql;
            }
        }

        /// <inheritdoc />
        public override string DisplayName
        {
            get
            {
                return "MySQL";
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
                return true;
            }
        }

        /// <inheritdoc />
        public override bool SupportsExplainAnalyze
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc />
        public override bool ExplainOfWritesNeedsWritableSession
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc />
        public override string VersionSql
        {
            get
            {
                return "SELECT VERSION()";
            }
        }

        /// <inheritdoc />
        public override string ListSchemasSql
        {
            get
            {
                return "SELECT schema_name, default_character_set_name AS charset FROM information_schema.schemata "
                    + "WHERE schema_name NOT IN ('mysql', 'information_schema', 'performance_schema', 'sys') ORDER BY schema_name";
            }
        }

        #endregion

        #region Private-Members

        private readonly MysqlSchemaCrawler _Crawler = new MysqlSchemaCrawler();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string BuildConnectionString(SqlResourceOptions options, bool readOnly)
        {
            ArgumentNullException.ThrowIfNull(options);
            MySqlConnectionStringBuilder builder;
            if (!String.IsNullOrEmpty(options.ConnectionString))
            {
                builder = new MySqlConnectionStringBuilder(options.ConnectionString);
            }
            else
            {
                if (String.IsNullOrEmpty(options.Host)) throw new ConnectorException("setting_missing", "Required setting 'host' is not set.");
                builder = new MySqlConnectionStringBuilder();
                builder.Server = options.Host;
                builder.Port = (uint)(options.Port ?? 3306);
                if (!String.IsNullOrEmpty(options.Database)) builder.Database = options.Database;
                if (!String.IsNullOrEmpty(options.Username)) builder.UserID = options.Username;
                if (!String.IsNullOrEmpty(options.Password)) builder.Password = options.Password;
                builder.ConnectionTimeout = (uint)options.ConnectTimeoutSeconds;
                switch (NormalizeSslMode(options.SslMode))
                {
                    case "Disable":
                        builder.SslMode = MySqlSslMode.None;
                        break;
                    case "Require":
                        builder.SslMode = MySqlSslMode.Required;
                        break;
                    case "VerifyCA":
                        builder.SslMode = MySqlSslMode.VerifyCA;
                        break;
                    case "VerifyFull":
                        builder.SslMode = MySqlSslMode.VerifyFull;
                        break;
                    default:
                        builder.SslMode = MySqlSslMode.Preferred;
                        break;
                }
            }
            if (String.IsNullOrEmpty(builder.ApplicationName)) builder.ApplicationName = options.ApplicationName;
            builder.DefaultCommandTimeout = (uint)options.CommandTimeoutSeconds;
            return builder.ToString();
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(SqlResourceOptions options, bool readOnly)
        {
            return new MySqlConnection(BuildConnectionString(options, readOnly));
        }

        /// <inheritdoc />
        public override async Task<DbTransaction?> BeginReadOnlyAsync(DbConnection connection, CancellationToken token = default)
        {
            MySqlConnection mysql = connection as MySqlConnection ?? throw new ArgumentException("Expected a MySqlConnection.", nameof(connection));
            return await mysql.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override string DefaultSchema(SqlResourceOptions options, DbConnection connection)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!String.IsNullOrEmpty(options.Schema)) return options.Schema;
            if (connection != null && !String.IsNullOrEmpty(connection.Database)) return connection.Database;
            return options.Database ?? String.Empty;
        }

        /// <inheritdoc />
        public override async Task<SqlQueryResult> ActivityAsync(SqlSession session, bool includeIdle, int maxRows, int timeoutSeconds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            SqlQueryResult result = await SqlExecutor.ExecuteAsync(session, "SHOW FULL PROCESSLIST", null, 100000, timeoutSeconds, token).ConfigureAwait(false);
            int command = result.ColumnIndex("Command");
            int info = result.ColumnIndex("Info");
            int user = result.ColumnIndex("User");
            result.Rows = result.Rows
                .Where(r => info < 0 || !String.Equals(r[info], "SHOW FULL PROCESSLIST", StringComparison.OrdinalIgnoreCase))
                .Where(r => user < 0 || !String.Equals(r[user], "event_scheduler", StringComparison.OrdinalIgnoreCase))
                .Where(r => includeIdle || command < 0 || !String.Equals(r[command], "Sleep", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (result.Rows.Count > maxRows)
            {
                result.Rows = result.Rows.Take(maxRows).ToList();
                result.Truncated = true;
            }
            result.RowLimit = maxRows;
            return result;
        }

        /// <inheritdoc />
        public override bool IsReadOnlyViolation(DbException exception)
        {
            return exception is MySqlException mysql && mysql.Number == 1792;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string ExplainPrefix(bool analyze)
        {
            return analyze ? "EXPLAIN ANALYZE " : "EXPLAIN ";
        }

        /// <inheritdoc />
        protected override string? TableSizesSql()
        {
            return "SELECT table_name, table_rows AS row_estimate, data_length + index_length AS total_bytes, "
                + "data_length AS table_bytes, index_length AS index_bytes, engine "
                + "FROM information_schema.tables WHERE table_schema = @schema AND table_type = 'BASE TABLE' "
                + "ORDER BY total_bytes DESC, table_name";
        }

        /// <inheritdoc />
        protected override string? LocksSql()
        {
            return "SELECT t.PROCESSLIST_ID AS connection_id, dl.OBJECT_SCHEMA AS object_schema, dl.OBJECT_NAME AS object_name, "
                + "dl.INDEX_NAME AS index_name, dl.LOCK_TYPE AS lock_type, dl.LOCK_MODE AS lock_mode, dl.LOCK_STATUS AS lock_status, "
                + "dl.LOCK_DATA AS lock_data "
                + "FROM performance_schema.data_locks dl LEFT JOIN performance_schema.threads t ON t.THREAD_ID = dl.THREAD_ID "
                + "WHERE t.PROCESSLIST_ID IS NULL OR t.PROCESSLIST_ID <> CONNECTION_ID() "
                + "ORDER BY dl.LOCK_STATUS DESC, connection_id";
        }

        /// <inheritdoc />
        protected override string? BlockingSql()
        {
            return "SELECT waiting_pid, blocking_pid, wait_age, locked_table, locked_type, waiting_lock_mode, blocking_lock_mode, "
                + "LEFT(waiting_query, 150) AS waiting_query, LEFT(blocking_query, 150) AS blocking_query "
                + "FROM sys.innodb_lock_waits ORDER BY wait_age_secs DESC";
        }

        #endregion
    }
}
