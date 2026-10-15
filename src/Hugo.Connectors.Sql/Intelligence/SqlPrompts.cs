// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6, the
// prompt builders of src/Tablix.Server/Handlers/ChatHandler.cs (MandatoryExecutionSystemPrompt, BuildPrompt,
// DefaultContextBuildInstructions, DefaultTableContextBuildInstructions, BuildContextPrompt, BuildTableContextPrompt,
// BuildFallbackPlannerPrompt, BuildToolFollowupPrompt). Copyright (c) 2026 Joel Christner and Adam Wingate, MIT
// License. Modified for Hugo: one-shot JSON planning (no native tool calls), read-only only, dialect notes and the
// row cap in the prompt, relevant tables only for large schemas, inferred relationships and intelligence included,
// a small masked row sample for table context (never persisted), clarification and schema-only answers in the plan,
// and an answer prompt that may only use the returned rows. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// Prompts for context generation and question answering. Prompts carry structure, statistics, context, and (for
    /// table context) a small masked row sample; never credentials. Thread-safe.
    /// </summary>
    public static class SqlPrompts
    {
        #region Public-Members

        /// <summary>
        /// Purpose label for database context generation.
        /// </summary>
        public const string PurposeDatabaseContext = "sql.generate-database-context";

        /// <summary>
        /// Purpose label for table context generation.
        /// </summary>
        public const string PurposeTableContext = "sql.generate-table-context";

        /// <summary>
        /// Purpose label for choosing relevant tables.
        /// </summary>
        public const string PurposeSelectTables = "sql.select-tables";

        /// <summary>
        /// Purpose label for planning SQL from a question.
        /// </summary>
        public const string PurposePlan = "sql.nl2sql";

        /// <summary>
        /// Purpose label for repairing SQL after an error.
        /// </summary>
        public const string PurposeRetry = "sql.nl2sql-retry";

        /// <summary>
        /// Purpose label for writing the answer from result rows.
        /// </summary>
        public const string PurposeAnswer = "sql.answer";

        /// <summary>
        /// System prompt for database context generation.
        /// </summary>
        public const string DatabaseContextSystemPrompt = "You write concise, durable database context for Hugo, an agent server. Restrict output to the selected database, its structure, contents, and relationships. Do not include credentials, secrets, raw result rows, personal data, or speculative facts. Clearly label inferred relationships as inferred.";

        /// <summary>
        /// System prompt for table context generation.
        /// </summary>
        public const string TableContextSystemPrompt = "You write concise, durable table context for Hugo, an agent server. Restrict output to the selected table, its structure, contents, and relationships. Do not include credentials, secrets, raw result rows, personal data, or speculative facts. Clearly label inferred relationships as inferred.";

        /// <summary>
        /// System prompt for SQL planning.
        /// </summary>
        public const string PlanSystemPrompt = "You translate a question about one database into exactly one read-only SQL statement that Hugo will run and verify. Use only the tables and columns listed. Never invent tables, columns, values, or results. If the question is ambiguous in a way that changes the SQL and no assumption resolves it, ask one clarification question instead of guessing. Return only JSON.";

        /// <summary>
        /// System prompt for answer writing.
        /// </summary>
        public const string AnswerSystemPrompt = "You answer a question about a database using only the query result provided. State values exactly as they appear in the result. Never invent rows, values, names, dates, counts, or totals that are not in the result. If the result is empty, say that no matching rows were found. If the result was truncated, say the answer covers only the returned rows. Keep the answer short and direct; do not tell the user to run a query.";

        /// <summary>
        /// System prompt for table selection.
        /// </summary>
        public const string SelectSystemPrompt = "You choose which tables a SQL query needs to answer a question. Return only JSON.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tablix's default instructions for database context.
        /// </summary>
        /// <returns>Instructions.</returns>
        public static string DefaultDatabaseInstructions()
        {
            return "Analyze the crawled schema and produce durable database context. Include the database purpose when inferable, major entities, important relationships, workflow groupings, naming conventions, business definitions an analyst would need (for example what counts as revenue or an active record, when the schema makes it clear), and safe query guidance. Clearly label inferred relationships. Keep the result concise enough to use in future model prompts.";
        }

        /// <summary>
        /// Tablix's default instructions for table context.
        /// </summary>
        /// <returns>Instructions.</returns>
        public static string DefaultTableInstructions()
        {
            return "Analyze the selected table and produce concise durable table context. Include the table purpose, key columns and what their values mean, the primary key, important foreign keys or inferred join paths, common filters, write-safety caveats, and how the table relates to the database. Describe value patterns from the sample (formats, enumerations, units) without copying rows. Clearly label inferred relationships and avoid secrets, personal data, or raw data.";
        }

        /// <summary>
        /// Build the database context prompt.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="detail">Crawl.</param>
        /// <param name="intelligence">Intelligence.</param>
        /// <param name="knowledge">Durable context.</param>
        /// <param name="instructions">Extra instructions from the caller, or null.</param>
        /// <param name="maxTables">Maximum tables described.</param>
        /// <returns>Prompt.</returns>
        public static string BuildDatabaseContextPrompt(SqlDialect dialect, SchemaDetail detail, DatabaseIntelligence intelligence, SchemaKnowledge knowledge, string? instructions, int maxTables)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Build durable context for this database using only the crawl below and the instructions.");
            b.AppendLine("Output only the context text to save, as Markdown without a wrapping code fence. Do not include credentials, raw data values, or personal data.");
            b.AppendLine();
            b.AppendLine("Instructions:");
            b.AppendLine(DefaultDatabaseInstructions());
            if (!String.IsNullOrWhiteSpace(instructions)) b.AppendLine(instructions.Trim());
            b.AppendLine();
            AppendDatabase(b, dialect, detail);
            b.AppendLine("Current saved database context:");
            b.AppendLine(knowledge.HasDatabaseContext ? knowledge.Database!.Content : "(none)");
            b.AppendLine();
            AppendIntelligence(b, intelligence, null);
            b.AppendLine("Crawled schema:");
            List<TableDetail> tables = detail.Tables.OrderBy(t => t.SchemaName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, maxTables)).ToList();
            foreach (TableDetail table in tables) AppendTable(b, table, knowledge, false);
            if (detail.Tables.Count > tables.Count)
                b.AppendLine("Additional tables (" + (detail.Tables.Count - tables.Count) + ") were omitted. Mention that the context may be partial.");
            b.AppendLine();
            AppendRelationships(b, intelligence.Relationships, 60);
            return b.ToString();
        }

        /// <summary>
        /// Build the table context prompt.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="detail">Crawl.</param>
        /// <param name="table">Selected table.</param>
        /// <param name="intelligence">Intelligence.</param>
        /// <param name="knowledge">Durable context.</param>
        /// <param name="sample">Masked sample rows, or null.</param>
        /// <param name="instructions">Extra instructions from the caller, or null.</param>
        /// <returns>Prompt.</returns>
        public static string BuildTableContextPrompt(SqlDialect dialect, SchemaDetail detail, TableDetail table, DatabaseIntelligence intelligence, SchemaKnowledge knowledge, SqlQueryResult? sample, string? instructions)
        {
            StringBuilder b = new StringBuilder();
            string key = TableIdentity.Key(table);
            b.AppendLine("Build durable context for exactly one table using only the crawl, statistics, and sample below and the instructions.");
            b.AppendLine("Output only the table context text to save, as Markdown without a wrapping code fence. Do not include credentials, secrets, raw rows, personal data, or unrelated commentary.");
            b.AppendLine();
            b.AppendLine("Instructions:");
            b.AppendLine(DefaultTableInstructions());
            if (!String.IsNullOrWhiteSpace(instructions)) b.AppendLine(instructions.Trim());
            b.AppendLine();
            AppendDatabase(b, dialect, detail);
            b.AppendLine("Database context:");
            b.AppendLine(knowledge.HasDatabaseContext ? knowledge.Database!.Content : "(none)");
            b.AppendLine();
            b.AppendLine("Selected table: " + key);
            ResourceKnowledgeEntry? existing = knowledge.Table(table.SchemaName, table.TableName);
            b.AppendLine("- Existing table context: " + (existing == null ? "(none)" : existing.Content));
            AppendTable(b, table, null, true);
            DomainEntity? entity = intelligence.Domain.Entities.FirstOrDefault(e => String.Equals(TableIdentity.Key(e.SchemaName, e.TableName), key, StringComparison.OrdinalIgnoreCase));
            if (entity != null) b.AppendLine("- Inferred role: " + entity.Role);
            List<string> metrics = intelligence.Domain.MetricDetails.Where(m => m.Column.StartsWith(key + ".", StringComparison.OrdinalIgnoreCase)).Select(m => m.Column + " (" + String.Join("/", m.Aggregations) + ")").ToList();
            if (metrics.Count > 0) b.AppendLine("- Likely metrics: " + String.Join("; ", metrics));
            List<string> filters = intelligence.Domain.CommonFilters.Where(f => f.StartsWith(key + ".", StringComparison.OrdinalIgnoreCase)).ToList();
            if (filters.Count > 0) b.AppendLine("- Likely filters: " + String.Join("; ", filters));
            b.AppendLine();
            List<RelationshipDetail> edges = intelligence.Relationships.Where(r => Touches(r, table)).ToList();
            AppendRelationships(b, edges, 40);

            if (sample != null && sample.Columns.Count > 0)
            {
                b.AppendLine("Sample rows (" + sample.RowCount + ", for understanding value formats only; sensitive columns are masked; never copy rows into the context):");
                b.AppendLine(sample.RenderTable(60));
            }
            else
            {
                b.AppendLine("Sample rows: (not available)");
            }
            b.AppendLine();
            b.AppendLine("Other tables for relationship context:");
            foreach (TableDetail related in detail.Tables.Where(t => !ReferenceEquals(t, table)).OrderBy(t => t.SchemaName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).Take(100))
                b.AppendLine("- " + TableIdentity.Key(related) + ": " + String.Join(", ", related.Columns.Select(c => c.ColumnName)));
            return b.ToString();
        }

        /// <summary>
        /// Build the table selection prompt for large schemas.
        /// </summary>
        /// <param name="question">Question.</param>
        /// <param name="detail">Crawl.</param>
        /// <param name="knowledge">Durable context.</param>
        /// <param name="maxTables">Maximum tables to choose.</param>
        /// <param name="maxIndexTables">Maximum tables listed in the index.</param>
        /// <returns>Prompt.</returns>
        public static string BuildSelectTablesPrompt(string question, SchemaDetail detail, SchemaKnowledge knowledge, int maxTables, int maxIndexTables)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Question: " + question);
            b.AppendLine();
            if (knowledge.HasDatabaseContext)
            {
                b.AppendLine("Database context:");
                b.AppendLine(AgentPackBuilder.Shorten(knowledge.Database!.Content, 2000, out _));
                b.AppendLine();
            }
            b.AppendLine("Tables (name: columns | context):");
            foreach (TableDetail t in detail.Tables.OrderBy(t => t.SchemaName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).Take(maxIndexTables))
            {
                string line = "- " + TableIdentity.Key(t) + ": " + String.Join(", ", t.Columns.Take(30).Select(c => c.ColumnName));
                ResourceKnowledgeEntry? entry = knowledge.Table(t.SchemaName, t.TableName);
                if (entry != null) line += " | " + AgentPackBuilder.Shorten(entry.Content.Replace('\n', ' '), 160, out _);
                b.AppendLine(line);
            }
            b.AppendLine();
            b.AppendLine("Choose the tables needed to answer the question, including any tables needed to join them. Return only JSON: {\"tables\":[\"schema.table\", ...]} with at most " + maxTables + " tables.");
            return b.ToString();
        }

        /// <summary>
        /// Build the SQL planning prompt.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="detail">Crawl.</param>
        /// <param name="tables">Tables relevant to the question.</param>
        /// <param name="intelligence">Intelligence.</param>
        /// <param name="knowledge">Durable context.</param>
        /// <param name="question">Question.</param>
        /// <param name="assumptions">Assumptions and clarifications from the caller.</param>
        /// <param name="ambiguities">Ambiguity signals that the assumptions are meant to resolve.</param>
        /// <param name="maxRows">Row cap the result will be cut to.</param>
        /// <returns>Prompt.</returns>
        public static string BuildPlanPrompt(SqlDialect dialect, SchemaDetail detail, List<TableDetail> tables, DatabaseIntelligence intelligence, SchemaKnowledge knowledge, string question, List<string> assumptions, List<AmbiguitySignal> ambiguities, int maxRows)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Write one read-only SQL statement that answers the question from the database described below.");
            b.AppendLine("Rules:");
            b.AppendLine("- Use the database and table context as authoritative business definitions. Use only the tables and columns listed under Schema, with their exact names.");
            b.AppendLine("- Only SELECT (or WITH ... SELECT). Never write, lock, or call procedures. One statement, no semicolon.");
            b.AppendLine("- Select only the columns needed, with clear aliases. For counts use COUNT(*) with an alias. Aggregate in SQL instead of returning raw rows.");
            b.AppendLine("- Results are cut at " + maxRows.ToString(CultureInfo.InvariantCulture) + " rows, so return at most that many rows (" + dialect.LimitSyntax + ") unless the query aggregates to fewer.");
            b.AppendLine("- For parent and detail questions (such as recent orders and what was purchased), limit the parent rows in a CTE or subquery first, then join detail rows; aggregate detail rows when one row per parent is expected.");
            b.AppendLine("- For relative date windows on static data, anchor the window to the latest relevant date in the data when the real current date would return nothing, and say so in reason.");
            b.AppendLine("- Prefer declared relationships for joins. Use an inferred relationship only when no declared path exists, and mention it in reason.");
            b.AppendLine("- If the user names a table that exists, use it; do not silently substitute another.");
            b.AppendLine("- If the question is ambiguous in a way the context and assumptions do not settle, return a clarification instead of SQL.");
            b.AppendLine("- If the question asks only about structure (which tables or columns exist) and needs no rows, you may answer from the schema without SQL.");
            b.AppendLine();
            AppendDatabase(b, dialect, detail);
            b.AppendLine("Dialect notes: " + dialect.PromptNotes);
            b.AppendLine();
            b.AppendLine("Database context:");
            b.AppendLine(knowledge.HasDatabaseContext ? knowledge.Database!.Content : "(none)");
            b.AppendLine();
            AppendIntelligence(b, intelligence, tables);
            b.AppendLine("Schema (" + tables.Count + " of " + detail.Tables.Count + " tables):");
            foreach (TableDetail table in tables) AppendTable(b, table, knowledge, false);
            if (tables.Count < detail.Tables.Count)
            {
                List<string> others = detail.Tables.Where(t => !tables.Contains(t)).Select(TableIdentity.Key).Take(200).ToList();
                b.AppendLine("Other tables not shown: " + String.Join(", ", others) + (detail.Tables.Count - tables.Count > others.Count ? ", ..." : String.Empty));
            }
            b.AppendLine();
            List<RelationshipDetail> edges = intelligence.Relationships.Where(r => tables.Any(t => Touches(r, t))).ToList();
            AppendRelationships(b, edges, 80);

            if (ambiguities.Count > 0)
            {
                b.AppendLine("Ambiguous terms in this question:");
                foreach (AmbiguitySignal a in ambiguities) b.AppendLine("- " + a.Term + ": " + a.Question + " Candidates: " + String.Join("; ", a.Candidates));
                b.AppendLine();
            }
            if (assumptions.Count > 0)
            {
                b.AppendLine("Assumptions and clarifications from the user (apply them):");
                foreach (string a in assumptions) b.AppendLine("- " + a);
                b.AppendLine();
            }

            b.AppendLine("Question: " + question);
            b.AppendLine();
            b.AppendLine("Return only compact JSON in one of these shapes:");
            b.AppendLine("{\"execute\":true,\"sql\":\"SELECT ...\",\"tables\":[\"schema.table\"],\"reason\":\"why this answers the question\",\"assumptions\":[\"any assumption you made\"]}");
            b.AppendLine("{\"execute\":false,\"ambiguous\":true,\"clarification\":\"one targeted question for the user\"}");
            b.AppendLine("{\"execute\":false,\"answer\":\"answer from the schema alone, only for structure questions\"}");
            return b.ToString();
        }

        /// <summary>
        /// Build the repair prompt after a failed execution.
        /// </summary>
        /// <param name="planPrompt">Original planning prompt (rebuilt from a fresh crawl when the error was about the schema).</param>
        /// <param name="sql">SQL that failed.</param>
        /// <param name="error">Error text from the database.</param>
        /// <returns>Prompt.</returns>
        public static string BuildRetryPrompt(string planPrompt, string sql, string error)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(planPrompt);
            b.AppendLine();
            b.AppendLine("Your previous SQL failed when Hugo ran it:");
            b.AppendLine(sql);
            b.AppendLine("Database error: " + error);
            b.AppendLine("Fix the SQL using only the listed tables and columns and return corrected JSON in the same shape.");
            return b.ToString();
        }

        /// <summary>
        /// Build the answer prompt from the executed result.
        /// </summary>
        /// <param name="question">Question.</param>
        /// <param name="assumptions">Assumptions applied.</param>
        /// <param name="sql">SQL that ran.</param>
        /// <param name="result">Result.</param>
        /// <param name="maxRows">Maximum rows shown to the model.</param>
        /// <returns>Prompt.</returns>
        public static string BuildAnswerPrompt(string question, List<string> assumptions, string sql, SqlQueryResult result, int maxRows)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Question: " + question);
            if (assumptions.Count > 0) b.AppendLine("Assumptions applied: " + String.Join("; ", assumptions));
            b.AppendLine("SQL that ran: " + sql);
            b.AppendLine("Rows returned: " + result.RowCount.ToString(CultureInfo.InvariantCulture) + (result.Truncated ? " (truncated at the row cap; more rows exist)" : " (complete result)"));
            SqlQueryResult shown = new SqlQueryResult { Columns = result.Columns, Rows = result.Rows.Take(maxRows).ToList() };
            if (result.RowCount > maxRows) b.AppendLine("Only the first " + maxRows + " rows are shown below.");
            b.AppendLine("Result:");
            b.AppendLine(shown.Columns.Count == 0 ? "(no columns)" : (shown.RowCount == 0 ? "(no rows)" : shown.RenderTable(200)));
            b.AppendLine();
            b.AppendLine("Answer the question using only this result.");
            return b.ToString();
        }

        #endregion

        #region Private-Methods

        private static void AppendDatabase(StringBuilder b, SqlDialect dialect, SchemaDetail detail)
        {
            b.AppendLine("Database:");
            b.AppendLine("- Engine: " + dialect.DisplayName);
            if (!String.IsNullOrWhiteSpace(detail.DatabaseName)) b.AppendLine("- Name: " + System.IO.Path.GetFileName(detail.DatabaseName));
            b.AppendLine("- Schema: " + detail.Schema);
            b.AppendLine("- Objects: " + detail.Tables.Count);
            b.AppendLine();
        }

        private static void AppendIntelligence(StringBuilder b, DatabaseIntelligence intelligence, List<TableDetail>? tables)
        {
            Func<string, bool> relevant = label => tables == null || tables.Any(t => label.StartsWith(TableIdentity.Key(t) + ".", StringComparison.OrdinalIgnoreCase));
            List<string> metrics = intelligence.Domain.MetricDetails.Where(m => relevant(m.Column)).Select(m => m.Column + " (" + String.Join("/", m.Aggregations) + (m.Note == null ? String.Empty : "; " + m.Note) + ")").Take(30).ToList();
            List<string> filters = intelligence.Domain.CommonFilters.Where(relevant).Take(30).ToList();
            List<string> freshness = intelligence.Domain.FreshnessColumns.Where(relevant).Take(30).ToList();
            List<string> softDelete = intelligence.Domain.SoftDeleteColumns.Where(relevant).Take(30).ToList();
            b.AppendLine("Intelligence:");
            b.AppendLine("- Likely metrics: " + (metrics.Count == 0 ? "(none)" : String.Join("; ", metrics)));
            b.AppendLine("- Common filters: " + (filters.Count == 0 ? "(none)" : String.Join("; ", filters)));
            b.AppendLine("- Freshness columns: " + (freshness.Count == 0 ? "(none)" : String.Join("; ", freshness)));
            if (softDelete.Count > 0) b.AppendLine("- Soft-delete columns (exclude deleted rows unless asked): " + String.Join("; ", softDelete));
            b.AppendLine();
        }

        private static void AppendTable(StringBuilder b, TableDetail table, SchemaKnowledge? knowledge, bool asSelected)
        {
            string prefix = asSelected ? "- " : "  ";
            if (!asSelected)
            {
                b.Append("- ").Append(TableIdentity.Key(table)).Append(" (").Append(table.TableType);
                if (table.RowEstimate.HasValue) b.Append(", ~").Append(table.RowEstimate.Value.ToString(CultureInfo.InvariantCulture)).Append(" rows");
                b.AppendLine(")");
                ResourceKnowledgeEntry? entry = knowledge?.Table(table.SchemaName, table.TableName);
                if (entry != null) b.AppendLine("  Table context: " + entry.Content.Replace("\n", "\n  "));
            }
            else
            {
                b.AppendLine("- Type: " + table.TableType + (table.RowEstimate.HasValue ? ", about " + table.RowEstimate.Value.ToString(CultureInfo.InvariantCulture) + " rows" : String.Empty));
            }
            b.AppendLine(prefix + "Columns: " + String.Join(", ", table.Columns.Select(c => c.ColumnName + " " + c.DataType + (c.IsPrimaryKey ? " primary key" : String.Empty) + (c.IsNullable ? " nullable" : " not null"))));
            if (table.ForeignKeys.Count > 0)
                b.AppendLine(prefix + "Declared FKs: " + String.Join("; ", table.ForeignKeys.Select(f => f.ColumnName + " -> " + f.ReferencedTable + "." + (f.ReferencedColumn ?? "(primary key)"))));
            if (table.Indexes.Count > 0)
                b.AppendLine(prefix + "Indexes: " + String.Join("; ", table.Indexes.Select(i => i.IndexName + "(" + String.Join(", ", i.Columns) + ")" + (i.IsUnique ? " unique" : String.Empty))));
        }

        private static void AppendRelationships(StringBuilder b, List<RelationshipDetail> edges, int max)
        {
            b.AppendLine("Relationships (declared are facts; inferred are candidates):");
            if (edges.Count == 0) b.AppendLine("- (none)");
            foreach (RelationshipDetail r in edges.Take(max)) b.AppendLine("- " + SchemaProjection.RenderLine(r));
            if (edges.Count > max) b.AppendLine("- " + (edges.Count - max) + " more omitted.");
            b.AppendLine();
        }

        private static bool Touches(RelationshipDetail r, TableDetail t)
        {
            return (String.Equals(r.FromTable, t.TableName, StringComparison.OrdinalIgnoreCase) && String.Equals(r.FromSchema, t.SchemaName, StringComparison.OrdinalIgnoreCase))
                || (String.Equals(r.ToTable, t.TableName, StringComparison.OrdinalIgnoreCase) && (r.ToSchema == null || String.Equals(r.ToSchema, t.SchemaName, StringComparison.OrdinalIgnoreCase)));
        }

        #endregion
    }
}
