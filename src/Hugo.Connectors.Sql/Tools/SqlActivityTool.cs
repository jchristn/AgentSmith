namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>sql_activity</c>: current sessions and running statements with blocking information
    /// (<c>pg_stat_activity</c>, <c>SHOW FULL PROCESSLIST</c>, <c>sys.dm_exec_requests</c>). Not applicable on SQLite.
    /// Thread-safe.
    /// </summary>
    public class SqlActivityTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlActivityTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            bool includeIdle = arguments.GetBool("includeIdle", false);
            int maxRows = Math.Max(context.Limits.MaxRows, 1);
            return await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
            {
                SqlQueryResult result = await Dialect.ActivityAsync(session, includeIdle, maxRows, timeoutSeconds, ct).ConfigureAwait(false);
                if (result.Columns.Count > 0 && result.RowCount == 0) result.Note = includeIdle ? "No other sessions." : "No active statements from other sessions. Pass includeIdle=true to list idle sessions.";
                return Bounded(context, Render(result, "session"), result, result.RowCount);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_activity",
                Title = "Database activity",
                Description = "List other sessions and their running statements: user, state, wait event, elapsed time, the sessions blocking them, and the statement text. "
                    + "Use it first for 'the database is slow' or 'queries hang' questions, then sql_locks for lock details. Idle sessions are hidden unless includeIdle is true. "
                    + "Not applicable to SQLite.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"includeIdle\":{\"type\":\"boolean\",\"description\":\"Include idle sessions. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table of sessions; structured content with columns and rows."
            };
        }

        #endregion
    }
}
