namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// Engine-specific behavior: connection strings, read-only sessions, parameters, cancellation, catalog crawling,
    /// and the SQL behind the operational tools. Stateless and thread-safe.
    /// </summary>
    public abstract class SqlDialect
    {
        #region Public-Members

        /// <summary>
        /// Engine.
        /// </summary>
        public abstract SqlEngineEnum Engine { get; }

        /// <summary>
        /// Display name (for example <c>PostgreSQL</c>).
        /// </summary>
        public abstract string DisplayName { get; }

        /// <summary>
        /// Catalog crawler.
        /// </summary>
        public abstract ISchemaCrawler Crawler { get; }

        /// <summary>
        /// Whether the engine can enforce read-only access for a session.
        /// </summary>
        public abstract bool SupportsSessionReadOnly { get; }

        /// <summary>
        /// Whether <c>sql_explain</c> can run with <c>analyze</c>.
        /// </summary>
        public virtual bool SupportsExplainAnalyze
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Whether the engine refuses <c>EXPLAIN</c> of a data-modifying statement inside a read-only transaction even
        /// though the statement is not executed (MySQL). When true, <c>sql_explain</c> without <c>analyze</c> runs such
        /// plans in an ordinary session; the statement is still only compiled.
        /// </summary>
        public virtual bool ExplainOfWritesNeedsWritableSession
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Query returning one row with the server version.
        /// </summary>
        public abstract string VersionSql { get; }

        /// <summary>
        /// Query listing schemas (databases for MySQL, attached databases for SQLite).
        /// </summary>
        public abstract string ListSchemasSql { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the connection string. The result contains secrets; never log or return it.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="readOnly">Whether to open a read-only connection where the engine supports it at connection
        /// level (SQLite).</param>
        /// <returns>Connection string.</returns>
        /// <exception cref="Hugo.Connectors.Abstractions.ConnectorException">Thrown with code <c>setting_missing</c>
        /// when required settings are absent.</exception>
        public abstract string BuildConnectionString(SqlResourceOptions options, bool readOnly);

        /// <summary>
        /// Create an unopened connection.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="readOnly">Whether to open read-only where supported at connection level.</param>
        /// <returns>Connection; the caller disposes it.</returns>
        public abstract DbConnection CreateConnection(SqlResourceOptions options, bool readOnly);

        /// <summary>
        /// Start a read-only transaction on an open connection, when the engine enforces read-only access that way.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The transaction, or null when read-only access is enforced by the connection itself.</returns>
        public virtual Task<DbTransaction?> BeginReadOnlyAsync(DbConnection connection, CancellationToken token = default)
        {
            return Task.FromResult<DbTransaction?>(null);
        }

        /// <summary>
        /// Default schema for catalog tools.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="connection">Open connection.</param>
        /// <returns>Schema name.</returns>
        public abstract string DefaultSchema(SqlResourceOptions options, DbConnection connection);

        /// <summary>
        /// Create a bound parameter. The SQL refers to it as <c>@name</c>.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <param name="value">Parameter value.</param>
        /// <returns>Parameter.</returns>
        public virtual DbParameter CreateParameter(DbCommand command, SqlParameterValue value)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(value);
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "@" + value.Name;
            parameter.Value = value.Value ?? DBNull.Value;
            return parameter;
        }

        /// <summary>
        /// Register an engine-level interrupt for a running command. The default relies on the driver honoring the
        /// token passed to its async methods.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="command">Running command.</param>
        /// <param name="token">Token.</param>
        /// <returns>Registration to dispose when the command completes.</returns>
        public virtual CancellationTokenRegistration RegisterInterrupt(DbConnection connection, DbCommand command, CancellationToken token)
        {
            return default;
        }

        /// <summary>
        /// Stop the server from producing rows after the row cap was reached, so the driver does not drain them.
        /// </summary>
        /// <param name="command">Command whose reader is still open.</param>
        public virtual void CancelRemainder(DbCommand command)
        {
            if (command == null) return;
            try
            {
                command.Cancel();
            }
            catch (Exception e) when (e is DbException || e is InvalidOperationException || e is NotSupportedException)
            {
            }
        }

        /// <summary>
        /// Run <c>EXPLAIN</c> for a statement.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="sql">Statement to explain.</param>
        /// <param name="analyze">Whether to execute and measure (only when <see cref="SupportsExplainAnalyze"/>).</param>
        /// <param name="parameters">Parameters, or null.</param>
        /// <param name="maxRows">Row cap.</param>
        /// <param name="timeoutSeconds">Command timeout.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Plan rows.</returns>
        public virtual Task<SqlQueryResult> ExplainAsync(SqlSession session, string sql, bool analyze, IList<SqlParameterValue>? parameters, int maxRows, int timeoutSeconds, CancellationToken token = default)
        {
            return SqlExecutor.ExecuteAsync(session, ExplainPrefix(analyze && SupportsExplainAnalyze) + sql, parameters, maxRows, timeoutSeconds, token);
        }

        /// <summary>
        /// List current sessions and running statements.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="includeIdle">Whether to include idle sessions.</param>
        /// <param name="maxRows">Row cap.</param>
        /// <param name="timeoutSeconds">Command timeout.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows, or a result with a note when not applicable.</returns>
        public virtual async Task<SqlQueryResult> ActivityAsync(SqlSession session, bool includeIdle, int maxRows, int timeoutSeconds, CancellationToken token = default)
        {
            string? sql = ActivitySql(includeIdle);
            if (sql == null) return NotApplicable(DisplayName + " has no server sessions to inspect; activity is not applicable.");
            return await SqlExecutor.ExecuteAsync(session, sql, null, maxRows, timeoutSeconds, token).ConfigureAwait(false);
        }

        /// <summary>
        /// List table sizes in a schema, largest first.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="schema">Schema.</param>
        /// <param name="maxRows">Row cap.</param>
        /// <param name="timeoutSeconds">Command timeout.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        public virtual async Task<SqlQueryResult> TableSizesAsync(SqlSession session, string schema, int maxRows, int timeoutSeconds, CancellationToken token = default)
        {
            string? sql = TableSizesSql();
            if (sql == null) return NotApplicable("Table sizes are not available for " + DisplayName + ".");
            List<SqlParameterValue> parameters = new List<SqlParameterValue> { new SqlParameterValue("schema", schema) };
            return await SqlExecutor.ExecuteAsync(session, sql, parameters, maxRows, timeoutSeconds, token).ConfigureAwait(false);
        }

        /// <summary>
        /// List locks and blocking relationships.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="maxRows">Row cap per section.</param>
        /// <param name="timeoutSeconds">Command timeout.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sections (for example Locks and Blocking).</returns>
        public virtual async Task<List<SqlQueryResult>> LocksAsync(SqlSession session, int maxRows, int timeoutSeconds, CancellationToken token = default)
        {
            List<SqlQueryResult> sections = new List<SqlQueryResult>();
            string? locks = LocksSql();
            string? blocking = BlockingSql();
            if (locks == null && blocking == null)
            {
                sections.Add(NotApplicable(DisplayName + " has no lock manager views; locks are not applicable."));
                return sections;
            }
            if (blocking != null)
            {
                SqlQueryResult b = await SqlExecutor.ExecuteAsync(session, blocking, null, maxRows, timeoutSeconds, token).ConfigureAwait(false);
                b.Title = "Blocking";
                sections.Add(b);
            }
            if (locks != null)
            {
                SqlQueryResult l = await SqlExecutor.ExecuteAsync(session, locks, null, maxRows, timeoutSeconds, token).ConfigureAwait(false);
                l.Title = "Locks";
                sections.Add(l);
            }
            return sections;
        }

        /// <summary>
        /// Whether a database error means the engine refused a write in a read-only session.
        /// </summary>
        /// <param name="exception">Database error.</param>
        /// <returns>True for a read-only violation.</returns>
        public virtual bool IsReadOnlyViolation(DbException exception)
        {
            return false;
        }

        /// <summary>
        /// Normalize the raw version value for display.
        /// </summary>
        /// <param name="raw">Raw value.</param>
        /// <returns>Display version.</returns>
        public virtual string FormatVersion(string raw)
        {
            if (String.IsNullOrEmpty(raw)) return String.Empty;
            int newline = raw.IndexOf('\n');
            return (newline < 0 ? raw : raw.Substring(0, newline)).Trim();
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Prefix that turns a statement into its plan query (for example <c>EXPLAIN </c>).
        /// </summary>
        /// <param name="analyze">Whether to execute and measure.</param>
        /// <returns>Prefix including a trailing space.</returns>
        protected abstract string ExplainPrefix(bool analyze);

        /// <summary>
        /// Activity query, or null when not applicable.
        /// </summary>
        /// <param name="includeIdle">Whether to include idle sessions.</param>
        /// <returns>SQL or null.</returns>
        protected virtual string? ActivitySql(bool includeIdle)
        {
            return null;
        }

        /// <summary>
        /// Table size query taking <c>@schema</c>, or null when not applicable.
        /// </summary>
        /// <returns>SQL or null.</returns>
        protected virtual string? TableSizesSql()
        {
            return null;
        }

        /// <summary>
        /// Lock listing query, or null when not applicable.
        /// </summary>
        /// <returns>SQL or null.</returns>
        protected virtual string? LocksSql()
        {
            return null;
        }

        /// <summary>
        /// Blocking chain query, or null when not applicable.
        /// </summary>
        /// <returns>SQL or null.</returns>
        protected virtual string? BlockingSql()
        {
            return null;
        }

        /// <summary>
        /// Build an empty result carrying a note.
        /// </summary>
        /// <param name="note">Note.</param>
        /// <returns>Result.</returns>
        protected static SqlQueryResult NotApplicable(string note)
        {
            return new SqlQueryResult { Note = note };
        }

        /// <summary>
        /// Map the shared <c>sslMode</c> setting to a canonical value.
        /// </summary>
        /// <param name="value">Setting value.</param>
        /// <returns>One of Disable, Prefer, Require, VerifyCA, VerifyFull.</returns>
        protected static string NormalizeSslMode(string? value)
        {
            if (String.IsNullOrEmpty(value)) return "Prefer";
            string v = value.Trim();
            if (v.Equals("Disable", StringComparison.OrdinalIgnoreCase) || v.Equals("None", StringComparison.OrdinalIgnoreCase) || v.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return "Disable";
            if (v.Equals("Require", StringComparison.OrdinalIgnoreCase) || v.Equals("Required", StringComparison.OrdinalIgnoreCase)) return "Require";
            if (v.Equals("VerifyCA", StringComparison.OrdinalIgnoreCase)) return "VerifyCA";
            if (v.Equals("VerifyFull", StringComparison.OrdinalIgnoreCase)) return "VerifyFull";
            return "Prefer";
        }

        #endregion
    }
}
