namespace Hugo.Connectors.Sql
{
    using Microsoft.Data.Sqlite;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// SQLite dialect. Read-only access is enforced with <c>Mode=ReadOnly</c> on the connection; cancellation and
    /// timeouts interrupt the running statement with <c>sqlite3_interrupt</c>. Stateless and thread-safe.
    /// </summary>
    public class SqliteDialect : SqlDialect
    {
        #region Public-Members

        /// <inheritdoc />
        public override SqlEngineEnum Engine
        {
            get
            {
                return SqlEngineEnum.Sqlite;
            }
        }

        /// <inheritdoc />
        public override string DisplayName
        {
            get
            {
                return "SQLite";
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
        public override string VersionSql
        {
            get
            {
                return "SELECT sqlite_version()";
            }
        }

        /// <inheritdoc />
        public override string ListSchemasSql
        {
            get
            {
                return "SELECT name AS schema_name, file FROM pragma_database_list ORDER BY seq";
            }
        }

        #endregion

        #region Private-Members

        private readonly SqliteSchemaCrawler _Crawler = new SqliteSchemaCrawler();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string BuildConnectionString(SqlResourceOptions options, bool readOnly)
        {
            ArgumentNullException.ThrowIfNull(options);
            SqliteConnectionStringBuilder builder;
            if (!String.IsNullOrEmpty(options.ConnectionString))
            {
                builder = new SqliteConnectionStringBuilder(options.ConnectionString);
            }
            else
            {
                if (String.IsNullOrEmpty(options.Filename)) throw new ConnectorException("setting_missing", "Required setting 'filename' is not set.");
                builder = new SqliteConnectionStringBuilder();
                builder.DataSource = options.Filename;
                builder.Mode = SqliteOpenMode.ReadWrite;
            }
            if (readOnly) builder.Mode = SqliteOpenMode.ReadOnly;
            builder.DefaultTimeout = options.CommandTimeoutSeconds;
            return builder.ToString();
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(SqlResourceOptions options, bool readOnly)
        {
            return new SqliteConnection(BuildConnectionString(options, readOnly));
        }

        /// <inheritdoc />
        public override string DefaultSchema(SqlResourceOptions options, DbConnection connection)
        {
            ArgumentNullException.ThrowIfNull(options);
            return String.IsNullOrEmpty(options.Schema) ? "main" : options.Schema;
        }

        /// <inheritdoc />
        public override DbParameter CreateParameter(DbCommand command, SqlParameterValue value)
        {
            DbParameter parameter = base.CreateParameter(command, value);
            parameter.ParameterName = value.Name;
            return parameter;
        }

        /// <inheritdoc />
        public override CancellationTokenRegistration RegisterInterrupt(DbConnection connection, DbCommand command, CancellationToken token)
        {
            SqliteConnection? sqlite = connection as SqliteConnection;
            if (sqlite == null || !token.CanBeCanceled) return default;
            return token.Register(() =>
            {
                try
                {
                    SQLitePCL.sqlite3? handle = sqlite.Handle;
                    if (handle != null) SQLitePCL.raw.sqlite3_interrupt(handle);
                }
                catch (Exception e) when (e is ObjectDisposedException || e is InvalidOperationException)
                {
                }
            });
        }

        /// <inheritdoc />
        public override void CancelRemainder(DbCommand command)
        {
        }

        /// <inheritdoc />
        public override async Task<SqlQueryResult> TableSizesAsync(SqlSession session, string schema, int maxRows, int timeoutSeconds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            List<SqlParameterValue> parameters = new List<SqlParameterValue> { new SqlParameterValue("schema", schema) };
            try
            {
                return await SqlExecutor.ExecuteAsync(
                    session,
                    "SELECT name AS object_name, SUM(pgsize) AS total_bytes, COUNT(*) AS pages FROM dbstat WHERE schema = @schema "
                    + "AND substr(name, 1, 7) <> 'sqlite_' GROUP BY name ORDER BY total_bytes DESC",
                    parameters,
                    maxRows,
                    timeoutSeconds,
                    token).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
            }

            List<TableDetail> tables = await _Crawler.ListTablesAsync(session.Connection, session.Transaction, schema, token).ConfigureAwait(false);
            SqlQueryResult result = new SqlQueryResult { RowLimit = maxRows };
            result.Columns.Add(new SqlColumnInfo("table_name", "TEXT"));
            result.Columns.Add(new SqlColumnInfo("row_count", "INTEGER"));
            foreach (TableDetail table in tables)
            {
                if (!String.Equals(table.TableType, "BASE TABLE", StringComparison.Ordinal)) continue;
                if (result.Rows.Count >= maxRows)
                {
                    result.Truncated = true;
                    break;
                }
                TableDetail? described = await _Crawler.DescribeTableAsync(session.Connection, session.Transaction, schema, table.TableName, token).ConfigureAwait(false);
                result.Rows.Add(new List<string?> { table.TableName, described?.RowEstimate?.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }

            object? pageCount = await ScalarAsync(session, "PRAGMA page_count", token).ConfigureAwait(false);
            object? pageSize = await ScalarAsync(session, "PRAGMA page_size", token).ConfigureAwait(false);
            long total = Convert.ToInt64(pageCount ?? 0L, System.Globalization.CultureInfo.InvariantCulture) * Convert.ToInt64(pageSize ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
            result.Note = "Per-table byte sizes need the dbstat virtual table, which this SQLite build lacks. Database file size: " + total + " bytes.";
            return result;
        }

        /// <inheritdoc />
        public override bool IsReadOnlyViolation(DbException exception)
        {
            return exception is SqliteException sqlite && (sqlite.SqliteErrorCode & 0xFF) == 8;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string ExplainPrefix(bool analyze)
        {
            return "EXPLAIN QUERY PLAN ";
        }

        #endregion

        #region Private-Methods

        private static async Task<object?> ScalarAsync(SqlSession session, string sql, CancellationToken token)
        {
            using (DbCommand command = session.CreateCommand(sql))
            {
                return await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
