namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Intelligence;

    /// <summary>
    /// <c>sql_list_tables</c>: a paged, compact index of the tables and views in a schema (Tablix
    /// <c>tablix_list_tables</c>), one line per table with row estimate, column count, key summary, and a marker when
    /// durable table context exists. Reads the shared full-crawl cache. Thread-safe.
    /// </summary>
    public class SqlListTablesTool : SqlToolBase
    {
        #region Public-Members

        /// <summary>
        /// Default page size when the caller gives none (further capped by the invocation row limit). Minimum 1,
        /// maximum 1000, default 100.
        /// </summary>
        public int DefaultPageSize
        {
            get
            {
                return _DefaultPageSize;
            }
            set
            {
                _DefaultPageSize = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private int _DefaultPageSize = 100;

        #endregion

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
            string? search = arguments.GetString("search") ?? arguments.GetString("filter");
            string? cursor = arguments.GetString("cursor");
            int pageSize = Math.Min(arguments.GetInt("pageSize", _DefaultPageSize, 1, 1000), Math.Max(1, context.Limits.MaxRows));

            return await WithCrawlAsync(context, arguments, async (snapshot, ct) =>
            {
                SchemaKnowledge knowledge = await LoadKnowledgeAsync(context, ct).ConfigureAwait(false);
                SchemaPage<TableSummary> page = SchemaProjection.CreateTableListResult(snapshot.Detail, knowledge, pageSize, cursor, search, null);

                StringBuilder sb = new StringBuilder();
                sb.Append("Tables in ").Append(page.Schema).Append(" (").Append(page.TableCount).Append(" objects");
                if (page.Search != null) sb.Append(", ").Append(page.TotalRecords).Append(" matching '").Append(page.Search).Append('\'');
                if (page.Items.Count > 0)
                {
                    int first = SchemaProjection.ParseCursor(cursor) + 1;
                    sb.Append("; showing ").Append(first).Append('-').Append(first + page.Items.Count - 1).Append(" of ").Append(page.TotalRecords);
                }
                sb.Append(snapshot.Cached ? "; crawl from cache, refresh=true recrawls" : String.Empty).Append(")\n");
                foreach (TableSummary t in page.Items) sb.Append(SchemaProjection.RenderLine(t)).Append('\n');
                if (page.TotalRecords == 0)
                    sb.Append(page.Search == null ? "No tables or views found in schema '" + page.Schema + "'. Use sql_list_schemas to see other schemas.\n" : "No tables or columns match '" + page.Search + "'.\n");
                if (!page.EndOfResults) sb.Append("More tables exist: call again with cursor \"").Append(page.NextCursor).Append("\".\n");
                sb.Append("Use sql_describe_table for full columns, keys, and indexes; sql_get_context for table meaning.\n");

                ToolResult result = BoundedPayload(context, sb.ToString(), page, page.Items.Count);
                result.Metrics["cached"] = snapshot.Cached ? 1 : 0;
                result.Metrics["totalRecords"] = page.TotalRecords;
                return result;
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
                Description = "Compact, paged index of the tables and views in a schema: one line per table with estimated rows (exact on SQLite), column count, primary and foreign keys, and a marker when durable table context exists. "
                    + "Preferred discovery tool for large databases. Narrow with search (matches table, schema, and column names) and page with cursor until no nextCursor is returned. "
                    + "The index is not enough to write SQL: call sql_describe_table for the tables you will query. Reads the cached crawl; refresh=true recrawls after DDL changes.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Defaults to the resource's default schema.\"},"
                    + "\"search\":{\"type\":\"string\",\"description\":\"Case-insensitive text matched against table, schema, and column names. Optional.\"},"
                    + "\"filter\":{\"type\":\"string\",\"description\":\"Older name for search. Optional.\"},"
                    + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from the previous page. Omit for the first page.\"},"
                    + "\"pageSize\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Tables per page. Default 100, capped by the row limit.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl instead of using the cached crawl. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "One line per table; structured content { schema, search, tableCount, totalRecords, pageSize, cursor, nextCursor, endOfResults, crawledUtc, items: [{ schemaName, tableName, tableType, rowEstimate, columns, foreignKeys, indexes, primaryKey, keySummary, hasContext, contextSource }] }."
            };
        }

        #endregion
    }
}
