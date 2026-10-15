namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>sql_table_sizes</c>: table sizes in a schema, largest first. Thread-safe.
    /// </summary>
    public class SqlTableSizesTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlTableSizesTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            int maxRows = Math.Max(context.Limits.MaxRows, 1);
            if (arguments.Has("limit")) maxRows = Math.Min(maxRows, arguments.GetInt("limit", maxRows, 1, 100000));
            return await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
            {
                string schema = ResolveSchema(session, arguments);
                SqlQueryResult result = await Dialect.TableSizesAsync(session, schema, maxRows, timeoutSeconds, ct).ConfigureAwait(false);
                result.Title = "Table sizes in " + schema;
                return Bounded(context, Render(result, "table"), result, result.RowCount);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_table_sizes",
                Title = "Table sizes",
                Description = "List tables in a schema by size, largest first, with row estimates and table and index bytes where the engine reports them. "
                    + "Use it for disk usage and growth questions instead of counting rows yourself.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Optional.\"},"
                    + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100000,\"description\":\"Maximum tables to return. Optional.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table of sizes; structured content with columns and rows."
            };
        }

        #endregion
    }
}
