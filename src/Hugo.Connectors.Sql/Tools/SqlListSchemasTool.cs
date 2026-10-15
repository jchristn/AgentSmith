namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>sql_list_schemas</c>: list schemas (databases on MySQL, attached databases on SQLite). Thread-safe.
    /// </summary>
    public class SqlListSchemasTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlListSchemasTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
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
                SqlQueryResult result = await SqlExecutor.ExecuteAsync(session, Dialect.ListSchemasSql, null, maxRows, timeoutSeconds, ct).ConfigureAwait(false);
                result.Note = "Default schema: " + Dialect.DefaultSchema(session.Options, session.Connection);
                return Bounded(context, Render(result, "schema"), result, result.RowCount);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_list_schemas",
                Title = "List schemas",
                Description = "List the schemas in the database (on MySQL, the databases; on SQLite, main and attached databases) and name the default schema. Start here when you do not know where tables live.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table of schemas; structured content with columns and rows."
            };
        }

        #endregion
    }
}
