namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>sql_locks</c>: blocking chains and held or awaited locks per engine. Not applicable on SQLite. Thread-safe.
    /// </summary>
    public class SqlLocksTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlLocksTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            int maxRows = Math.Max(context.Limits.MaxRows, 1);
            return await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
            {
                List<SqlQueryResult> sections = await Dialect.LocksAsync(session, maxRows, timeoutSeconds, ct).ConfigureAwait(false);
                StringBuilder sb = new StringBuilder();
                foreach (SqlQueryResult section in sections)
                {
                    if (section.Columns.Count > 0 && section.RowCount == 0)
                        section.Note = String.Equals(section.Title, "Blocking", StringComparison.Ordinal) ? "No session is blocked." : "No locks held by other sessions.";
                    sb.Append(Render(section, "row")).Append('\n');
                }
                SqlSectionsResult payload = new SqlSectionsResult { Sections = sections };
                return BoundedPayload(context, sb.ToString(), payload, sections.Sum(s => s.RowCount));
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_locks",
                Title = "Locks and blocking",
                Description = "Show which sessions are blocked and by whom, then the locks other sessions hold or wait for (pg_locks, performance_schema.data_locks with sys.innodb_lock_waits, sys.dm_tran_locks). "
                    + "Use it after sql_activity shows waiting sessions. Not applicable to SQLite.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Blocking and Locks sections; structured content { sections: [...] }."
            };
        }

        #endregion
    }
}
