namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// <c>sql_describe_table</c>: columns, types, nullability, keys, indexes, foreign keys, and row estimate of one
    /// table or view. Thread-safe.
    /// </summary>
    public class SqlDescribeTableTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlDescribeTableTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            string? table = arguments.GetString("table");
            if (String.IsNullOrWhiteSpace(table)) return ToolResult.Error("invalid_argument", "Argument 'table' is required.");
            table = table.Trim();
            string? explicitSchema = arguments.GetString("schema");
            if (String.IsNullOrWhiteSpace(explicitSchema) && table.Count(ch => ch == '.') == 1)
            {
                int dot = table.IndexOf('.');
                explicitSchema = table.Substring(0, dot);
                table = table.Substring(dot + 1);
            }
            string tableName = table;

            return await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
            {
                string schema = String.IsNullOrWhiteSpace(explicitSchema) ? ResolveSchema(session, arguments) : explicitSchema.Trim();
                TableDetail? detail = await Dialect.Crawler.DescribeTableAsync(session.Connection, session.Transaction, schema, tableName, ct).ConfigureAwait(false);
                if (detail == null)
                    return ToolResult.Error("not_found", "Table or view '" + tableName + "' was not found in schema '" + schema + "'. Use sql_list_tables to see what exists.");
                return BoundedPayload(context, RenderTable(detail), detail, detail.Columns.Count);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Render a table description as compact text.
        /// </summary>
        /// <param name="detail">Table.</param>
        /// <returns>Text.</returns>
        public static string RenderTable(TableDetail detail)
        {
            ArgumentNullException.ThrowIfNull(detail);
            StringBuilder sb = new StringBuilder();
            sb.Append(detail.SchemaName).Append('.').Append(detail.TableName).Append(" (").Append(detail.TableType);
            if (detail.RowEstimate.HasValue) sb.Append(", about ").Append(detail.RowEstimate.Value.ToString(CultureInfo.InvariantCulture)).Append(" rows");
            sb.Append(")\n\nColumns:\n");

            List<string> columns = new List<string> { "column", "type", "nullable", "default", "key" };
            List<IList<string?>> rows = new List<IList<string?>>();
            HashSet<string> fkColumns = new HashSet<string>(detail.ForeignKeys.Select(f => f.ColumnName), StringComparer.OrdinalIgnoreCase);
            foreach (ColumnDetail c in detail.Columns)
            {
                string type = c.DataType + (c.MaxLength.HasValue && c.MaxLength.Value > 0 && !c.DataType.Contains('(') ? "(" + c.MaxLength.Value.ToString(CultureInfo.InvariantCulture) + ")" : String.Empty);
                string key = c.IsPrimaryKey ? "PK" : String.Empty;
                if (fkColumns.Contains(c.ColumnName)) key = key.Length > 0 ? key + ",FK" : "FK";
                rows.Add(new List<string?> { c.ColumnName, type, c.IsNullable ? "yes" : "no", c.DefaultValue ?? String.Empty, key });
            }
            sb.Append(TextTable.Render(columns, rows));

            List<string> pk = detail.Columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();
            sb.Append("\nPrimary key: ").Append(pk.Count > 0 ? String.Join(", ", pk) : "none").Append('\n');

            sb.Append("Foreign keys:");
            if (detail.ForeignKeys.Count == 0) sb.Append(" none\n");
            else
            {
                sb.Append('\n');
                foreach (ForeignKeyDetail fk in detail.ForeignKeys)
                {
                    sb.Append("- ").Append(fk.ConstraintName).Append(": ").Append(fk.ColumnName).Append(" -> ");
                    if (!String.IsNullOrEmpty(fk.ReferencedSchema)) sb.Append(fk.ReferencedSchema).Append('.');
                    sb.Append(fk.ReferencedTable).Append('(').Append(fk.ReferencedColumn ?? "primary key").Append(")\n");
                }
            }

            sb.Append("Indexes:");
            if (detail.Indexes.Count == 0) sb.Append(" none\n");
            else
            {
                sb.Append('\n');
                foreach (IndexDetail ix in detail.Indexes)
                {
                    sb.Append("- ").Append(ix.IndexName);
                    if (ix.IsPrimary) sb.Append(" PRIMARY");
                    else if (ix.IsUnique) sb.Append(" UNIQUE");
                    sb.Append(" (").Append(String.Join(", ", ix.Columns)).Append(")\n");
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_describe_table",
                Title = "Describe table",
                Description = "Describe one table or view: columns with types, nullability, and defaults; primary key; foreign keys; indexes; and the estimated row count. "
                    + "Use it before writing queries that join or filter the table. Accepts schema.table or a separate schema.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"table\":{\"type\":\"string\",\"description\":\"Table or view name, optionally schema-qualified.\"},"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Optional.\"}"
                    + "},\"required\":[\"table\"],\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text description; structured content { schemaName, tableName, tableType, rowEstimate, columns, foreignKeys, indexes }."
            };
        }

        #endregion
    }
}
