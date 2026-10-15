namespace Hugo.Connectors.Sql
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;
    using Npgsql;
    using NpgsqlTypes;

    /// <summary>
    /// PostgreSQL dialect. Read-only access is enforced with <c>SET TRANSACTION READ ONLY</c> inside a transaction that
    /// is rolled back when the call ends. Stateless and thread-safe.
    /// </summary>
    public class PostgresqlDialect : SqlDialect
    {
        #region Public-Members

        /// <inheritdoc />
        public override SqlEngineEnum Engine
        {
            get
            {
                return SqlEngineEnum.Postgresql;
            }
        }

        /// <inheritdoc />
        public override string DisplayName
        {
            get
            {
                return "PostgreSQL";
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
        public override string VersionSql
        {
            get
            {
                return "SHOW server_version";
            }
        }

        /// <inheritdoc />
        public override string ListSchemasSql
        {
            get
            {
                return "SELECT schema_name, schema_owner FROM information_schema.schemata "
                    + "WHERE schema_name NOT IN ('pg_catalog', 'information_schema') AND schema_name NOT LIKE 'pg\\_toast%' "
                    + "AND schema_name NOT LIKE 'pg\\_temp%' ORDER BY schema_name";
            }
        }

        #endregion

        #region Private-Members

        private readonly PostgresqlSchemaCrawler _Crawler = new PostgresqlSchemaCrawler();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string BuildConnectionString(SqlResourceOptions options, bool readOnly)
        {
            ArgumentNullException.ThrowIfNull(options);
            NpgsqlConnectionStringBuilder builder;
            if (!String.IsNullOrEmpty(options.ConnectionString))
            {
                builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            }
            else
            {
                if (String.IsNullOrEmpty(options.Host)) throw new ConnectorException("setting_missing", "Required setting 'host' is not set.");
                builder = new NpgsqlConnectionStringBuilder();
                builder.Host = options.Host;
                builder.Port = options.Port ?? 5432;
                if (!String.IsNullOrEmpty(options.Database)) builder.Database = options.Database;
                if (!String.IsNullOrEmpty(options.Username)) builder.Username = options.Username;
                if (!String.IsNullOrEmpty(options.Password)) builder.Password = options.Password;
                builder.Timeout = options.ConnectTimeoutSeconds;
                switch (NormalizeSslMode(options.SslMode))
                {
                    case "Disable":
                        builder.SslMode = SslMode.Disable;
                        break;
                    case "Require":
                        builder.SslMode = SslMode.Require;
                        break;
                    case "VerifyCA":
                        builder.SslMode = SslMode.VerifyCA;
                        break;
                    case "VerifyFull":
                        builder.SslMode = SslMode.VerifyFull;
                        break;
                    default:
                        builder.SslMode = SslMode.Prefer;
                        break;
                }
            }
            if (String.IsNullOrEmpty(builder.ApplicationName)) builder.ApplicationName = options.ApplicationName;
            builder.CommandTimeout = options.CommandTimeoutSeconds;
            return builder.ToString();
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(SqlResourceOptions options, bool readOnly)
        {
            return new NpgsqlConnection(BuildConnectionString(options, readOnly));
        }

        /// <inheritdoc />
        public override async Task<DbTransaction?> BeginReadOnlyAsync(DbConnection connection, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            try
            {
                using (DbCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SET TRANSACTION READ ONLY";
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            catch
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            return transaction;
        }

        /// <inheritdoc />
        public override string DefaultSchema(SqlResourceOptions options, DbConnection connection)
        {
            ArgumentNullException.ThrowIfNull(options);
            return String.IsNullOrEmpty(options.Schema) ? "public" : options.Schema;
        }

        /// <inheritdoc />
        public override DbParameter CreateParameter(DbCommand command, SqlParameterValue value)
        {
            DbParameter parameter = base.CreateParameter(command, value);
            parameter.ParameterName = value.Name;
            if (value.Value is string && parameter is NpgsqlParameter npgsql) npgsql.NpgsqlDbType = NpgsqlDbType.Unknown;
            return parameter;
        }

        /// <inheritdoc />
        public override bool IsReadOnlyViolation(DbException exception)
        {
            return exception is PostgresException pg && pg.SqlState == "25006";
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string ExplainPrefix(bool analyze)
        {
            return analyze ? "EXPLAIN (ANALYZE, BUFFERS) " : "EXPLAIN ";
        }

        /// <inheritdoc />
        protected override string? ActivitySql(bool includeIdle)
        {
            return "SELECT pid, usename AS user_name, datname AS database_name, application_name, client_addr::text AS client, state, "
                + "wait_event_type, wait_event, round(EXTRACT(EPOCH FROM (now() - query_start))::numeric, 1) AS query_seconds, "
                + "pg_blocking_pids(pid)::text AS blocked_by, left(query, 300) AS query "
                + "FROM pg_stat_activity WHERE backend_type = 'client backend' AND pid <> pg_backend_pid() "
                + (includeIdle ? String.Empty : "AND state IS DISTINCT FROM 'idle' ")
                + "ORDER BY query_start NULLS LAST";
        }

        /// <inheritdoc />
        protected override string? TableSizesSql()
        {
            return "SELECT c.relname AS table_name, CASE WHEN c.reltuples < 0 THEN NULL ELSE c.reltuples::bigint END AS row_estimate, "
                + "pg_total_relation_size(c.oid) AS total_bytes, pg_relation_size(c.oid) AS table_bytes, "
                + "pg_indexes_size(c.oid) AS index_bytes, pg_size_pretty(pg_total_relation_size(c.oid)) AS total_size "
                + "FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "WHERE n.nspname = @schema AND c.relkind IN ('r', 'p', 'm') "
                + "ORDER BY pg_total_relation_size(c.oid) DESC, c.relname";
        }

        /// <inheritdoc />
        protected override string? LocksSql()
        {
            return "SELECT l.pid, a.usename AS user_name, l.locktype, l.mode, l.granted, c.relname AS relation, "
                + "pg_blocking_pids(l.pid)::text AS blocked_by, round(EXTRACT(EPOCH FROM (now() - a.query_start))::numeric, 1) AS query_seconds, "
                + "left(a.query, 200) AS query "
                + "FROM pg_locks l LEFT JOIN pg_class c ON c.oid = l.relation LEFT JOIN pg_stat_activity a ON a.pid = l.pid "
                + "WHERE l.pid IS DISTINCT FROM pg_backend_pid() AND l.locktype <> 'virtualxid' "
                + "ORDER BY l.granted, l.pid";
        }

        /// <inheritdoc />
        protected override string? BlockingSql()
        {
            return "SELECT blocked.pid AS blocked_pid, blocked.usename AS blocked_user, blocking.pid AS blocking_pid, "
                + "blocking.usename AS blocking_user, round(EXTRACT(EPOCH FROM (now() - blocked.query_start))::numeric, 1) AS waiting_seconds, "
                + "left(blocked.query, 150) AS blocked_query, left(blocking.query, 150) AS blocking_query "
                + "FROM pg_stat_activity blocked "
                + "CROSS JOIN LATERAL unnest(pg_blocking_pids(blocked.pid)) AS b(pid) "
                + "JOIN pg_stat_activity blocking ON blocking.pid = b.pid "
                + "ORDER BY waiting_seconds DESC NULLS LAST";
        }

        #endregion
    }
}
