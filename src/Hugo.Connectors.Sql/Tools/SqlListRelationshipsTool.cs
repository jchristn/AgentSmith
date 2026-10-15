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

    /// <summary>
    /// <c>sql_list_relationships</c>: a paged index of declared foreign keys and inferred join candidates (Tablix
    /// <c>tablix_list_relationships</c>), with confidence and evidence, filterable by table. Thread-safe.
    /// </summary>
    public class SqlListRelationshipsTool : SqlToolBase
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
        public SqlListRelationshipsTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            string? search = arguments.GetString("search");
            string? table = arguments.GetString("table");
            string? cursor = arguments.GetString("cursor");
            bool includeInferred = arguments.GetBool("includeInferred", true);
            int pageSize = Math.Min(arguments.GetInt("pageSize", _DefaultPageSize, 1, 1000), Math.Max(1, context.Limits.MaxRows));
            double minConfidence = 0;
            string? rawConfidence = arguments.GetString("minConfidence");
            if (rawConfidence != null)
            {
                if (!Double.TryParse(rawConfidence, NumberStyles.Float, CultureInfo.InvariantCulture, out minConfidence) || minConfidence < 0 || minConfidence > 1)
                    return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "minConfidence must be a number from 0 to 1.");
            }

            return await WithCrawlAsync(context, arguments, (snapshot, ct) =>
            {
                if (!String.IsNullOrWhiteSpace(table) && TableIdentity.Find(snapshot.Detail, table).Count == 0)
                    return Task.FromResult(ToolResult.Error(ConnectorErrorCodes.NotFound, "Table '" + table + "' was not found in schema '" + snapshot.Detail.Schema + "'. Use sql_list_tables to see what exists."));

                List<RelationshipDetail> relationships = DatabaseIntelligenceBuilder.BuildRelationships(snapshot.Detail, includeInferred);
                SchemaPage<RelationshipDetail> page = SchemaProjection.CreateRelationshipListResult(snapshot.Detail, relationships, pageSize, cursor, search, table, includeInferred, minConfidence);
                int declared = relationships.Count(r => r.IsDeclared);

                StringBuilder sb = new StringBuilder();
                sb.Append("Relationships in ").Append(page.Schema).Append(" (").Append(declared).Append(" declared, ").Append(relationships.Count - declared).Append(" inferred");
                if (page.Table != null || page.Search != null || minConfidence > 0) sb.Append("; ").Append(page.TotalRecords).Append(" match the filters");
                sb.Append(snapshot.Cached ? "; crawl from cache" : String.Empty).Append(")\n");
                foreach (RelationshipDetail r in page.Items) sb.Append(SchemaProjection.RenderLine(r)).Append('\n');
                if (page.TotalRecords == 0) sb.Append("No relationships match. Absence of a declared key does not prove tables are unrelated; inspect columns with sql_describe_table.\n");
                if (!page.EndOfResults) sb.Append("More relationships exist: call again with cursor \"").Append(page.NextCursor).Append("\".\n");
                if (page.Items.Any(r => !r.IsDeclared))
                    sb.Append("Inferred edges are candidates from naming; confirm them before relying on them, and record confirmed join paths with sql_update_context.\n");

                ToolResult result = BoundedPayload(context, sb.ToString(), page, page.Items.Count);
                result.Metrics["cached"] = snapshot.Cached ? 1 : 0;
                result.Metrics["totalRecords"] = page.TotalRecords;
                return Task.FromResult(result);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_list_relationships",
                Title = "List relationships",
                Description = "Paged index of join paths: declared foreign keys (source declared_fk, confidence 1.0) and inferred candidates from naming conventions (source inferred_name_match) with a confidence score and the evidence behind it "
                    + "(column named after a table, matching key name and type, unique target, index). Use it to plan joins, especially in databases without declared keys. "
                    + "Filter by table (either end), search, or minConfidence, and page with cursor. Treat inferred edges as candidates until confirmed.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Defaults to the resource's default schema.\"},"
                    + "\"table\":{\"type\":\"string\",\"description\":\"Only edges that start or end at this table (bare or schema-qualified). Optional.\"},"
                    + "\"search\":{\"type\":\"string\",\"description\":\"Case-insensitive text matched against tables, columns, and constraint names. Optional.\"},"
                    + "\"includeInferred\":{\"type\":\"boolean\",\"description\":\"Include inferred candidates. Default true.\"},"
                    + "\"minConfidence\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1,\"description\":\"Drop edges below this confidence. Default 0.\"},"
                    + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from the previous page. Omit for the first page.\"},"
                    + "\"pageSize\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Edges per page. Default 100, capped by the row limit.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl instead of using the cached crawl. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "One line per edge; structured content { schema, table, search, totalRecords, nextCursor, endOfResults, items: [{ fromSchema, fromTable, fromColumn, toSchema, toTable, toColumn, constraintName, source, confidence, evidence, isDeclared }] }."
            };
        }

        #endregion
    }
}
