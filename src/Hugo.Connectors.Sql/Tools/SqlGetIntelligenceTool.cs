namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Intelligence;

    /// <summary>
    /// <c>sql_get_intelligence</c>: domain entities, relationships, likely metrics, common filters, freshness columns,
    /// ambiguity signals, and the context quality score with its gaps (Tablix <c>tablix_get_database_intelligence</c>).
    /// Thread-safe.
    /// </summary>
    public class SqlGetIntelligenceTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlGetIntelligenceTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            return await WithCrawlAsync(context, arguments, async (snapshot, ct) =>
            {
                SchemaKnowledge knowledge = await LoadKnowledgeAsync(context, ct).ConfigureAwait(false);
                DatabaseIntelligence intelligence = DatabaseIntelligenceBuilder.Build(snapshot.Detail, knowledge);
                ToolResult result = BoundedPayload(context, Render(intelligence, snapshot.Cached), intelligence, intelligence.Domain.Entities.Count);
                result.Metrics["cached"] = snapshot.Cached ? 1 : 0;
                result.Metrics["contextQuality"] = intelligence.ContextQuality.Score;
                return result;
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Render intelligence as compact text.
        /// </summary>
        /// <param name="intelligence">Intelligence.</param>
        /// <param name="cached">Whether the crawl came from the cache.</param>
        /// <returns>Text.</returns>
        public static string Render(DatabaseIntelligence intelligence, bool cached)
        {
            ArgumentNullException.ThrowIfNull(intelligence);
            StringBuilder sb = new StringBuilder();
            sb.Append("Intelligence for schema ").Append(intelligence.Schema).Append(" (").Append(intelligence.Engine).Append(", crawled ")
                .Append(intelligence.CrawledUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" UTC").Append(cached ? ", from cache" : String.Empty).Append(")\n");
            ContextQualityScore q = intelligence.ContextQuality;
            sb.Append("Context quality: ").Append(q.Score).Append("/100 ").Append(q.Label).Append(" (database context ").Append(q.HasDatabaseContext ? "saved" : "missing")
                .Append(", ").Append(q.TablesWithContext).Append(" of ").Append(q.TotalTables).Append(" tables with context)\n");
            sb.Append(intelligence.Domain.Summary).Append("\n\nEntities:\n");
            foreach (DomainEntity e in intelligence.Domain.Entities)
            {
                sb.Append("- ").Append(TableIdentity.Key(e.SchemaName, e.TableName)).Append(": ").Append(e.Role);
                if (e.RowEstimate.HasValue) sb.Append(", ~").Append(e.RowEstimate.Value.ToString(CultureInfo.InvariantCulture)).Append(" rows");
                if (e.KeyColumns.Count > 0) sb.Append("; key columns ").Append(String.Join(", ", e.KeyColumns));
                if (e.HasContext) sb.Append("; context (").Append(e.ContextSource).Append(')');
                sb.Append('\n');
            }

            int declared = intelligence.Relationships.Count(r => r.IsDeclared);
            sb.Append("\nRelationships: ").Append(declared).Append(" declared, ").Append(intelligence.Relationships.Count - declared).Append(" inferred\n");
            foreach (RelationshipDetail r in intelligence.Relationships.Take(25)) sb.Append("- ").Append(SchemaProjection.RenderLine(r)).Append('\n');
            if (intelligence.Relationships.Count > 25) sb.Append("- ").Append(intelligence.Relationships.Count - 25).Append(" more; page with sql_list_relationships.\n");

            sb.Append("\nLikely metrics:\n");
            foreach (MetricCandidate m in intelligence.Domain.MetricDetails)
                sb.Append("- ").Append(m.Column).Append(": ").Append(String.Join(", ", m.Aggregations)).Append(m.Note == null ? String.Empty : ". " + m.Note).Append('\n');
            if (intelligence.Domain.MetricDetails.Count == 0) sb.Append("- none detected\n");
            AppendLine(sb, "Common filters", intelligence.Domain.CommonFilters);
            AppendLine(sb, "Freshness columns", intelligence.Domain.FreshnessColumns);
            AppendLine(sb, "Tenant columns", intelligence.Domain.TenantColumns);
            AppendLine(sb, "Soft-delete columns", intelligence.Domain.SoftDeleteColumns);

            sb.Append("\nAmbiguities to clarify before answering:\n");
            foreach (AmbiguitySignal a in intelligence.Ambiguities)
                sb.Append("- ").Append(a.Term).Append(": ").Append(a.Question).Append(" Candidates: ").Append(String.Join("; ", a.Candidates)).Append('\n');
            if (intelligence.Ambiguities.Count == 0) sb.Append("- none\n");

            if (q.Signals.Count > 0)
            {
                sb.Append("\nContext gaps:\n");
                foreach (ContextQualitySignal s in q.Signals) sb.Append("- ").Append(s.Severity).Append(": ").Append(s.Message).Append(' ').Append(s.Recommendation).Append('\n');
            }
            if (intelligence.SuggestedQuestions.Count > 0)
            {
                sb.Append("\nStarter questions:\n");
                foreach (string s in intelligence.SuggestedQuestions) sb.Append("- ").Append(s).Append('\n');
            }
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static void AppendLine(StringBuilder sb, string title, System.Collections.Generic.List<string> items)
        {
            sb.Append(title).Append(": ").Append(items.Count == 0 ? "none" : String.Join(", ", items)).Append('\n');
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_get_intelligence",
                Title = "Get database intelligence",
                Description = "Schema-to-domain intelligence for one schema: entities with their likely role (core entity, event, lookup, junction, view), declared and inferred relationships, likely metrics with natural aggregations, "
                    + "common filters (status, type, activity, tenancy, soft delete, dates), freshness columns, ambiguous terms to clarify before answering, and a 0 to 100 context quality score with the gaps that lower it. "
                    + "Use it for a compact domain readout; sql_get_agent_pack adds the saved context in one briefing.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Defaults to the resource's default schema.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl instead of using the cached crawl. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text readout; structured content { engine, databaseName, schema, crawledUtc, domain: { summary, entities, workflows, metrics, metricDetails, commonFilters, freshnessColumns, tenantColumns, softDeleteColumns }, relationships, ambiguities, contextQuality: { score, label, signals, ... }, suggestedQuestions }."
            };
        }

        #endregion
    }
}
