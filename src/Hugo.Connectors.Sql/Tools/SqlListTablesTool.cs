namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// <c>sql_list_tables</c>: list tables and views in a schema with row estimates. Thread-safe.
    /// </summary>
    public class SqlListTablesTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlListTablesTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            string? filter = arguments.GetString("filter");
            int maxRows = Math.Max(context.Limits.MaxRows, 1);

            return await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
            {
                string schema = ResolveSchema(session, arguments);
                List<TableDetail> tables = await Dialect.Crawler.ListTablesAsync(session.Connection, session.Transaction, schema, ct).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(filter))
                    tables = tables.Where(t => t.TableName.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

                SqlQueryResult result = new SqlQueryResult { Title = "Tables in " + schema, RowLimit = maxRows };
                result.Columns.Add(new SqlColumnInfo("table_name", "text"));
                result.Columns.Add(new SqlColumnInfo("type", "text"));
                result.Columns.Add(new SqlColumnInfo("row_estimate", "bigint"));
                foreach (TableDetail t in tables)
                {
                    if (result.Rows.Count >= maxRows)
                    {
                        result.Truncated = true;
                        break;
                    }
                    result.Rows.Add(new List<string?> { t.TableName, t.TableType, t.RowEstimate?.ToString(CultureInfo.InvariantCulture) });
                }
                if (tables.Count == 0) result.Note = "No tables or views found in schema '" + schema + "'. Use sql_list_schemas to see other schemas.";
                return Bounded(context, Render(result, "object"), result, result.RowCount);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_list_tables",
                Title = "List tables",
                Description = "List the tables and views in a schema with their type and estimated row counts (exact counts on SQLite). "
                    + "Use before writing a query so you use real table names. The schema defaults to the resource's default schema.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Optional.\"},"
                    + "\"filter\":{\"type\":\"string\",\"description\":\"Case-insensitive substring to match table names. Optional.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table of name, type, row estimate; structured content with columns and rows."
            };
        }

        #endregion
    }
}
