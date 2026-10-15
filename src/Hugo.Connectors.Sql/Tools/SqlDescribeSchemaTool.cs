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
    using Hugo.Connectors.Sql.Intelligence;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// <c>sql_describe_schema</c>: crawl every table and view of a schema, cached in process for the resource's
    /// <c>schemaCacheMinutes</c>. Thread-safe.
    /// </summary>
    public class SqlDescribeSchemaTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlDescribeSchemaTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            return await WithCrawlAsync(context, arguments, (snapshot, ct) => Task.FromResult(Build(context, snapshot.Detail, snapshot.Cached)), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Cache key for a resource and schema.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="schema">Schema.</param>
        /// <returns>Key.</returns>
        public static string CacheKey(ConnectorInvocationContext context, string schema)
        {
            return CrawlKey(context, schema);
        }

        #endregion

        #region Private-Methods

        private static ToolResult Build(ConnectorInvocationContext context, SchemaDetail detail, bool cached)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Schema ").Append(detail.Schema).Append(" (").Append(detail.Engine).Append(", ").Append(detail.Tables.Count).Append(" objects, crawled ")
                .Append(detail.CrawledUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" UTC")
                .Append(cached ? ", from cache; pass refresh=true to recrawl" : String.Empty).Append(")\n");
            foreach (TableDetail t in detail.Tables)
            {
                sb.Append('\n').Append(t.TableName).Append(" (").Append(t.TableType);
                if (t.RowEstimate.HasValue) sb.Append(", ~").Append(t.RowEstimate.Value.ToString(CultureInfo.InvariantCulture)).Append(" rows");
                sb.Append("): ");
                sb.Append(String.Join(", ", t.Columns.Select(c => c.ColumnName + " " + c.DataType + (c.IsPrimaryKey ? " PK" : String.Empty) + (c.IsNullable ? String.Empty : " NOT NULL"))));
                sb.Append('\n');
                foreach (ForeignKeyDetail fk in t.ForeignKeys)
                    sb.Append("  FK ").Append(fk.ColumnName).Append(" -> ").Append(fk.ReferencedTable).Append('(').Append(fk.ReferencedColumn ?? "primary key").Append(")\n");
                List<IndexDetail> secondary = t.Indexes.Where(i => !i.IsPrimary).ToList();
                if (secondary.Count > 0)
                    sb.Append("  Indexes: ").Append(String.Join("; ", secondary.Select(i => i.IndexName + (i.IsUnique ? " UNIQUE" : String.Empty) + " (" + String.Join(", ", i.Columns) + ")"))).Append('\n');
            }
            ToolResult result = BoundedPayload(context, sb.ToString(), detail, detail.Tables.Count);
            result.Metrics["cached"] = cached ? 1 : 0;
            return result;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_describe_schema",
                Title = "Describe schema",
                Description = "Crawl a whole schema: every table and view with columns, keys, foreign keys, indexes, and row estimates, in one compact listing. "
                    + "Results are cached for a few minutes and shared with the other discovery tools; pass refresh=true after DDL changes. On large schemas prefer sql_get_agent_pack, the paged sql_list_tables and sql_list_relationships, and sql_describe_table for single tables.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Optional.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Bypass the cache. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Compact schema listing; structured content { engine, databaseName, schema, tables: [...], crawledUtc }."
            };
        }

        #endregion
    }
}
