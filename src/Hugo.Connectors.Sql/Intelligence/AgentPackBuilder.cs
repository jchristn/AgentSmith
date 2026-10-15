// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// DatabaseIntelligenceBuilder.BuildAgentPack in src/Tablix.Core/Helpers/DatabaseIntelligenceBuilder.cs. Copyright (c)
// 2026 Joel Christner and Adam Wingate, MIT License. Modified for Hugo: Hugo tool names and workflow in the
// instructions, database and table context included (Tablix only linked to it), metrics with aggregation hints,
// filters, freshness, context gaps, and caps with omitted counts. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds the agent pack: one bounded Markdown briefing from intelligence and durable context. Thread-safe.
    /// </summary>
    public static class AgentPackBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build the pack.
        /// </summary>
        /// <param name="intelligence">Intelligence.</param>
        /// <param name="knowledge">Durable context, or null.</param>
        /// <param name="options">Caps, or null for defaults.</param>
        /// <returns>Pack.</returns>
        /// <exception cref="ArgumentNullException">Thrown when intelligence is null.</exception>
        public static AgentPack Build(DatabaseIntelligence intelligence, SchemaKnowledge? knowledge, AgentPackOptions? options)
        {
            ArgumentNullException.ThrowIfNull(intelligence);
            SchemaKnowledge k = knowledge ?? new SchemaKnowledge();
            AgentPackOptions o = options ?? new AgentPackOptions();

            AgentPack pack = new AgentPack
            {
                Engine = intelligence.Engine,
                DatabaseName = intelligence.DatabaseName,
                Schema = intelligence.Schema,
                GeneratedUtc = DateTime.UtcNow,
                ContextQuality = intelligence.ContextQuality,
                Ambiguities = intelligence.Ambiguities,
                SuggestedQuestions = intelligence.SuggestedQuestions,
                Instructions = BuildInstructions(o)
            };

            if (k.HasDatabaseContext)
            {
                pack.DatabaseContext = Shorten(k.Database!.Content, o.DatabaseContextChars, out _);
                pack.DatabaseContextSource = k.Database.Source.ToString();
            }

            DomainIntelligence domain = intelligence.Domain;
            List<DomainEntity> ranked = domain.Entities
                .OrderBy(e => RoleRank(e.Role))
                .ThenByDescending(e => e.RowEstimate ?? 0)
                .ThenBy(e => e.TableName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            pack.Domain = new DomainIntelligence
            {
                Summary = domain.Summary,
                Entities = ranked.Take(o.MaxEntities).ToList(),
                Workflows = domain.Workflows.Take(o.MaxRelationships).ToList(),
                Metrics = domain.Metrics,
                MetricDetails = domain.MetricDetails,
                CommonFilters = domain.CommonFilters,
                FreshnessColumns = domain.FreshnessColumns,
                TenantColumns = domain.TenantColumns,
                SoftDeleteColumns = domain.SoftDeleteColumns
            };
            pack.OmittedEntities = Math.Max(0, ranked.Count - o.MaxEntities);

            List<RelationshipDetail> edges = intelligence.Relationships
                .OrderByDescending(r => r.IsDeclared)
                .ThenByDescending(r => r.Confidence)
                .ThenBy(r => r.FromTable, StringComparer.OrdinalIgnoreCase)
                .ToList();
            pack.Relationships = edges.Take(o.MaxRelationships).ToList();
            pack.OmittedRelationships = Math.Max(0, edges.Count - o.MaxRelationships);

            List<string> tableOrder = ranked.Select(e => TableIdentity.Key(e.SchemaName, e.TableName)).ToList();
            List<ResourceKnowledgeEntry> contexts = k.Tables.Values
                .Where(e => tableOrder.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(e => tableOrder.FindIndex(t => String.Equals(t, e.Key, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            pack.TableContexts = contexts.Take(o.MaxTableContexts).Select(e => Summarize(e, o.TableContextChars)).ToList();
            pack.OmittedTableContexts = Math.Max(0, contexts.Count - o.MaxTableContexts);

            pack.Markdown = Render(pack, o);
            return pack;
        }

        /// <summary>
        /// Summarize one knowledge entry, shortening the content.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="maxChars">Maximum content characters.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public static TableContextSummary Summarize(ResourceKnowledgeEntry entry, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(entry);
            string content = Shorten(entry.Content, maxChars, out bool shortened);
            return new TableContextSummary
            {
                Table = entry.Key,
                Source = entry.Source.ToString(),
                UpdatedBy = entry.UpdatedBy,
                Version = entry.Version,
                LastUpdateUtc = entry.LastUpdateUtc,
                Content = content,
                Shortened = shortened
            };
        }

        /// <summary>
        /// Cut text to a character budget at a word boundary, adding an ellipsis marker.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="maxChars">Budget.</param>
        /// <param name="shortened">Whether the text was cut.</param>
        /// <returns>Text.</returns>
        public static string Shorten(string? text, int maxChars, out bool shortened)
        {
            shortened = false;
            string t = (text ?? String.Empty).Trim();
            if (t.Length <= maxChars) return t;
            shortened = true;
            int cut = t.LastIndexOf(' ', Math.Max(0, maxChars - 1));
            if (cut < maxChars / 2) cut = maxChars;
            return t.Substring(0, cut).TrimEnd() + " [...]";
        }

        #endregion

        #region Private-Methods

        private static List<string> BuildInstructions(AgentPackOptions o)
        {
            return new List<string>
            {
                "Treat the database and table context in this pack as authoritative business guidance, but verify table and column names against the schema before querying.",
                "On large schemas page through sql_list_tables and sql_list_relationships (search, table, cursor) instead of crawling everything; call sql_describe_table for every table you will select from, join, or filter on.",
                "Declared relationships are facts. Inferred relationships are candidates: confirm them (context, matching values, or the operator) before relying on them, and label them as inferred in answers.",
                "Send one read statement per sql_query call with explicit columns, selective filters, and " + o.LimitSyntax + "; aggregate in SQL instead of pulling rows. Truncated results are incomplete, so never total them yourself.",
                "When a question uses an ambiguous term listed below, ask the clarification question (or state the assumption you were given) before running SQL.",
                "For a plain-language question, prefer sql_ask when it is available: it checks ambiguity, plans guarded read-only SQL from this context, runs it, and returns a verified answer with evidence.",
                "Record durable discoveries (a confirmed join path, a business definition, a correction to stale context) with sql_update_context. Never store secrets, credentials, personal data, or raw result rows in context."
            };
        }

        private static string Render(AgentPack pack, AgentPackOptions o)
        {
            StringBuilder md = new StringBuilder();
            string title = String.IsNullOrWhiteSpace(pack.DatabaseName) || String.Equals(pack.DatabaseName, pack.Schema, StringComparison.OrdinalIgnoreCase)
                ? "schema " + pack.Schema
                : pack.DatabaseName + ", schema " + pack.Schema;
            md.Append("# SQL agent pack: ").Append(title).Append('\n');
            md.Append("Engine: ").Append(String.IsNullOrEmpty(o.EngineDisplayName) ? pack.Engine : o.EngineDisplayName)
                .Append(". Context quality: ").Append(pack.ContextQuality.Score.ToString(CultureInfo.InvariantCulture)).Append("/100 ").Append(pack.ContextQuality.Label).Append(".\n\n");

            md.Append("## How to work\n");
            foreach (string instruction in pack.Instructions) md.Append("- ").Append(instruction).Append('\n');

            md.Append("\n## Database context");
            if (pack.DatabaseContext == null) md.Append("\nNone saved yet. Generate it with sql_generate_context or write it with sql_update_context.\n");
            else md.Append(" (").Append(pack.DatabaseContextSource).Append(")\n").Append(pack.DatabaseContext).Append('\n');

            md.Append("\n## Domain\n").Append(pack.Domain.Summary).Append('\n');
            md.Append("\n## Entities\n");
            foreach (DomainEntity e in pack.Domain.Entities)
            {
                md.Append("- `").Append(TableIdentity.Key(e.SchemaName, e.TableName)).Append("` ").Append(e.Role);
                if (e.RowEstimate.HasValue) md.Append(", ~").Append(e.RowEstimate.Value.ToString(CultureInfo.InvariantCulture)).Append(" rows");
                if (e.KeyColumns.Count > 0) md.Append("; key columns: ").Append(String.Join(", ", e.KeyColumns));
                if (e.HasContext) md.Append("; has context");
                md.Append('\n');
            }
            if (pack.OmittedEntities > 0) md.Append("- ").Append(pack.OmittedEntities).Append(" more; page with sql_list_tables.\n");

            md.Append("\n## Relationships\n");
            foreach (RelationshipDetail r in pack.Relationships) md.Append("- ").Append(SchemaProjection.RenderLine(r)).Append('\n');
            if (pack.Relationships.Count == 0) md.Append("- No declared or inferred relationships were found.\n");
            if (pack.OmittedRelationships > 0) md.Append("- ").Append(pack.OmittedRelationships).Append(" more; page with sql_list_relationships.\n");

            if (pack.Domain.MetricDetails.Count > 0)
            {
                md.Append("\n## Likely metrics\n");
                foreach (MetricCandidate m in pack.Domain.MetricDetails.Take(20))
                {
                    md.Append("- ").Append(m.Column).Append(": ").Append(String.Join(", ", m.Aggregations));
                    if (m.Note != null) md.Append(". ").Append(m.Note);
                    md.Append('\n');
                }
            }
            AppendList(md, "Common filters", pack.Domain.CommonFilters);
            AppendList(md, "Freshness columns", pack.Domain.FreshnessColumns);
            AppendList(md, "Tenant columns", pack.Domain.TenantColumns);
            AppendList(md, "Soft-delete columns", pack.Domain.SoftDeleteColumns);

            md.Append("\n## Ambiguities to clarify\n");
            foreach (AmbiguitySignal a in pack.Ambiguities.Take(8))
                md.Append("- ").Append(a.Term).Append(": ").Append(a.Question).Append(" Candidates: ").Append(String.Join("; ", a.Candidates.Take(6))).Append('\n');
            if (pack.Ambiguities.Count == 0) md.Append("- None detected.\n");

            if (pack.TableContexts.Count > 0)
            {
                md.Append("\n## Table context\n");
                foreach (TableContextSummary t in pack.TableContexts)
                    md.Append("### ").Append(t.Table).Append(" (").Append(t.Source).Append(")\n").Append(t.Content).Append('\n');
                if (pack.OmittedTableContexts > 0) md.Append(pack.OmittedTableContexts).Append(" more table context(s); read them with sql_get_context.\n");
            }

            if (pack.ContextQuality.Signals.Count > 0)
            {
                md.Append("\n## Context gaps\n");
                foreach (ContextQualitySignal s in pack.ContextQuality.Signals)
                    md.Append("- ").Append(s.Severity).Append(": ").Append(s.Message).Append(' ').Append(s.Recommendation).Append('\n');
            }

            AppendList(md, "Starter questions", pack.SuggestedQuestions);
            return md.ToString();
        }

        private static void AppendList(StringBuilder md, string title, List<string> items)
        {
            if (items.Count == 0) return;
            md.Append("\n## ").Append(title).Append('\n');
            foreach (string item in items.Take(20)) md.Append("- ").Append(item).Append('\n');
            if (items.Count > 20) md.Append("- ").Append(items.Count - 20).Append(" more.\n");
        }

        private static int RoleRank(string role)
        {
            switch (role)
            {
                case "core entity": return 0;
                case "entity": return 1;
                case "event": return 2;
                case "junction": return 3;
                case "lookup": return 4;
                default: return 5;
            }
        }

        #endregion
    }
}
