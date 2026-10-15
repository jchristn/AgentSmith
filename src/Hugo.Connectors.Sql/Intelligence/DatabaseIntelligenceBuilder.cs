// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Helpers/DatabaseIntelligenceBuilder.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT
// License. Modified for Hugo: namespace and style; context comes from SchemaKnowledge (the resource knowledge store)
// instead of Context fields on the crawl; inference improvements (declared keys suppress inference from the same
// column, views are sources but never targets, prefixed table names such as legacy_accounts match account_id,
// composite primary keys are not guessed, type compatibility, uniqueness, and indexes adjust confidence, and every
// edge carries evidence); ambiguity improvements (terms resolve when the question names one candidate table or
// column, or when saved context defines the term; views are not party tables); metric aggregation hints; the agent
// pack moved to AgentPackBuilder. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// Builds heuristic intelligence from a crawled schema and the durable context: relationships (declared and
    /// inferred), domain entities, metrics, filters, freshness, ambiguity signals, and a context quality score.
    /// Deterministic and thread-safe.
    /// </summary>
    public static class DatabaseIntelligenceBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build whole-schema intelligence.
        /// </summary>
        /// <param name="detail">Crawl.</param>
        /// <param name="knowledge">Durable context, or null for none.</param>
        /// <returns>Intelligence.</returns>
        /// <exception cref="ArgumentNullException">Thrown when detail is null.</exception>
        public static DatabaseIntelligence Build(SchemaDetail detail, SchemaKnowledge? knowledge)
        {
            ArgumentNullException.ThrowIfNull(detail);
            SchemaKnowledge k = knowledge ?? new SchemaKnowledge();
            List<RelationshipDetail> relationships = BuildRelationships(detail, true);
            DomainIntelligence domain = BuildDomain(detail, relationships, k);
            List<AmbiguitySignal> ambiguities = BuildAmbiguities(detail, k, null);
            ContextQualityScore quality = BuildContextQuality(detail, relationships, ambiguities, k);

            return new DatabaseIntelligence
            {
                Engine = detail.Engine,
                DatabaseName = detail.DatabaseName,
                Schema = detail.Schema,
                CrawledUtc = detail.CrawledUtc,
                Domain = domain,
                Relationships = relationships,
                Ambiguities = ambiguities,
                ContextQuality = quality,
                SuggestedQuestions = BuildSuggestedQuestions(domain)
            };
        }

        /// <summary>
        /// Build declared and, optionally, inferred relationships, ordered by source table and column.
        /// </summary>
        /// <param name="detail">Crawl.</param>
        /// <param name="includeInferred">Whether to add name-based candidates.</param>
        /// <returns>Relationships.</returns>
        /// <exception cref="ArgumentNullException">Thrown when detail is null.</exception>
        public static List<RelationshipDetail> BuildRelationships(SchemaDetail detail, bool includeInferred)
        {
            ArgumentNullException.ThrowIfNull(detail);
            List<RelationshipDetail> relationships = BuildDeclaredRelationships(detail);
            if (includeInferred)
            {
                HashSet<string> existing = new HashSet<string>(relationships.Select(RelationshipKey), StringComparer.Ordinal);
                relationships.AddRange(BuildInferredRelationships(detail, existing));
            }

            return relationships
                .OrderBy(r => r.FromSchema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FromTable, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FromColumn, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Source, StringComparer.Ordinal)
                .ThenByDescending(r => r.Confidence)
                .ToList();
        }

        /// <summary>
        /// Ambiguity signals that apply to one question. A term applies when the question uses it, the schema offers
        /// more than one candidate, the question does not name exactly one candidate table or column, and saved
        /// context does not define it.
        /// </summary>
        /// <param name="detail">Crawl.</param>
        /// <param name="knowledge">Durable context, or null.</param>
        /// <param name="question">Question.</param>
        /// <returns>Signals; empty when the question is unambiguous.</returns>
        /// <exception cref="ArgumentNullException">Thrown when detail is null.</exception>
        public static List<AmbiguitySignal> FindPromptAmbiguities(SchemaDetail detail, SchemaKnowledge? knowledge, string? question)
        {
            ArgumentNullException.ThrowIfNull(detail);
            if (String.IsNullOrWhiteSpace(question)) return new List<AmbiguitySignal>();
            return BuildAmbiguities(detail, knowledge ?? new SchemaKnowledge(), question);
        }

        /// <summary>
        /// Qualified label of a table (<c>schema.table</c>).
        /// </summary>
        /// <param name="schemaName">Schema.</param>
        /// <param name="tableName">Table.</param>
        /// <returns>Label.</returns>
        public static string Qualify(string? schemaName, string? tableName)
        {
            return TableIdentity.Key(schemaName, tableName);
        }

        /// <summary>
        /// Whether a question mentions a table by name, by singular or plural, or by its name without a leading prefix
        /// word (<c>accounts</c> for <c>legacy_accounts</c>, <c>order totals</c> but not <c>totals</c> for
        /// <c>customer_order_totals</c>).
        /// </summary>
        /// <param name="question">Question.</param>
        /// <param name="tableName">Table name.</param>
        /// <returns>True when mentioned.</returns>
        public static bool MentionsTable(string? question, string? tableName)
        {
            if (String.IsNullOrWhiteSpace(question) || String.IsNullOrWhiteSpace(tableName)) return false;
            string text = " " + String.Join(" ", NameHeuristics.Words(question)) + " ";
            List<string> words = NameHeuristics.Words(tableName);
            for (int start = 0; start < Math.Min(2, words.Count); start++)
            {
                List<string> tail = words.Skip(start).ToList();
                foreach (string phrase in PhraseVariants(tail))
                {
                    if (text.Contains(" " + phrase + " ", StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether a question mentions a word or identifier (by its words, singular or plural).
        /// </summary>
        /// <param name="question">Question.</param>
        /// <param name="identifier">Identifier.</param>
        /// <returns>True when mentioned.</returns>
        public static bool MentionsIdentifier(string? question, string? identifier)
        {
            if (String.IsNullOrWhiteSpace(question) || String.IsNullOrWhiteSpace(identifier)) return false;
            string text = " " + String.Join(" ", NameHeuristics.Words(question)) + " ";
            string normalized = NameHeuristics.NormalizeIdentifier(identifier);
            if (text.Contains(" " + normalized + " ", StringComparison.Ordinal)) return true;
            return PhraseVariants(NameHeuristics.Words(identifier)).Any(p => text.Contains(" " + p + " ", StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        private static List<RelationshipDetail> BuildDeclaredRelationships(SchemaDetail detail)
        {
            List<RelationshipDetail> relationships = new List<RelationshipDetail>();
            foreach (TableDetail table in detail.Tables)
            {
                foreach (ForeignKeyDetail foreignKey in table.ForeignKeys)
                {
                    string targetSchema = String.IsNullOrWhiteSpace(foreignKey.ReferencedSchema) ? table.SchemaName : foreignKey.ReferencedSchema;
                    TableDetail? referenced = detail.Tables.FirstOrDefault(t =>
                        String.Equals(t.TableName, foreignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(t.SchemaName, targetSchema, StringComparison.OrdinalIgnoreCase))
                        ?? detail.Tables.FirstOrDefault(t => String.Equals(t.TableName, foreignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase));

                    string? toColumn = foreignKey.ReferencedColumn;
                    if (String.IsNullOrWhiteSpace(toColumn) && referenced != null)
                    {
                        List<ColumnDetail> pk = referenced.Columns.Where(c => c.IsPrimaryKey).ToList();
                        if (pk.Count == 1) toColumn = pk[0].ColumnName;
                    }

                    RelationshipDetail relationship = new RelationshipDetail
                    {
                        FromSchema = table.SchemaName,
                        FromTable = table.TableName,
                        FromColumn = foreignKey.ColumnName,
                        ToSchema = referenced?.SchemaName ?? targetSchema,
                        ToTable = referenced?.TableName ?? foreignKey.ReferencedTable,
                        ToColumn = toColumn,
                        ConstraintName = String.IsNullOrWhiteSpace(foreignKey.ConstraintName) ? null : foreignKey.ConstraintName,
                        Source = RelationshipDetail.DeclaredSource,
                        Confidence = 1.0
                    };
                    relationship.Evidence.Add(String.IsNullOrWhiteSpace(foreignKey.ConstraintName)
                        ? "Declared foreign key."
                        : "Declared foreign key constraint " + foreignKey.ConstraintName + ".");
                    relationships.Add(relationship);
                }
            }
            return relationships;
        }

        private static List<RelationshipDetail> BuildInferredRelationships(SchemaDetail detail, HashSet<string> existing)
        {
            List<RelationshipDetail> relationships = new List<RelationshipDetail>();
            List<TableDetail> targets = detail.Tables.Where(t => !IsView(t)).ToList();

            foreach (TableDetail sourceTable in detail.Tables)
            {
                HashSet<string> declaredColumns = new HashSet<string>(sourceTable.ForeignKeys.Select(f => f.ColumnName), StringComparer.OrdinalIgnoreCase);
                foreach (ColumnDetail sourceColumn in sourceTable.Columns)
                {
                    if (declaredColumns.Contains(sourceColumn.ColumnName)) continue;
                    string? entityToken = NameHeuristics.ForeignKeyEntityToken(sourceColumn.ColumnName);
                    if (String.IsNullOrWhiteSpace(entityToken)) continue;

                    foreach (TableDetail targetTable in targets)
                    {
                        if (ReferenceEquals(sourceTable, targetTable)) continue;
                        if (String.Equals(TableIdentity.Key(sourceTable), TableIdentity.Key(targetTable), StringComparison.OrdinalIgnoreCase)) continue;

                        string? matchedName = MatchEntityToken(targetTable.TableName, entityToken, out bool suffixMatch);
                        if (matchedName == null) continue;

                        ColumnDetail? targetColumn = PreferredKeyColumn(targetTable, sourceColumn.ColumnName);
                        if (targetColumn == null) continue;

                        RelationshipDetail relationship = new RelationshipDetail
                        {
                            FromSchema = sourceTable.SchemaName,
                            FromTable = sourceTable.TableName,
                            FromColumn = sourceColumn.ColumnName,
                            ToSchema = targetTable.SchemaName,
                            ToTable = targetTable.TableName,
                            ToColumn = targetColumn.ColumnName,
                            ConstraintName = null,
                            Source = RelationshipDetail.InferredSource
                        };
                        relationship.Confidence = InferredConfidence(sourceTable, sourceColumn, targetTable, targetColumn, matchedName, suffixMatch, relationship.Evidence);

                        string key = RelationshipKey(relationship);
                        if (existing.Contains(key)) continue;
                        existing.Add(key);
                        relationships.Add(relationship);
                    }
                }
            }
            return relationships;
        }

        private static double InferredConfidence(TableDetail sourceTable, ColumnDetail sourceColumn, TableDetail targetTable, ColumnDetail targetColumn, string matchedName, bool suffixMatch, List<string> evidence)
        {
            // Tablix base scores: 0.74 for a name match, 0.86 when the column is exactly <singular table>id, plus 0.04
            // when the target key is literally "id". Hugo adjusts with evidence the crawl already has.
            double confidence = 0.74;
            string source = NameHeuristics.NormalizeIdentifier(sourceColumn.ColumnName);
            string singularTarget = NameHeuristics.Singularize(matchedName);
            if (source == singularTarget + "id")
            {
                confidence = 0.86;
                evidence.Add("Column " + sourceColumn.ColumnName + " is named after table " + targetTable.TableName + ".");
            }
            else
            {
                evidence.Add("Column " + sourceColumn.ColumnName + " resembles table " + targetTable.TableName + ".");
            }

            if (String.Equals(targetColumn.ColumnName, "id", StringComparison.OrdinalIgnoreCase)) confidence += 0.04;
            if (String.Equals(targetColumn.ColumnName, sourceColumn.ColumnName, StringComparison.OrdinalIgnoreCase))
            {
                confidence += 0.04;
                evidence.Add("Same column name as the target key " + targetTable.TableName + "." + targetColumn.ColumnName + ".");
            }

            if (suffixMatch)
            {
                confidence -= 0.06;
                evidence.Add("Matched after ignoring the table name prefix (" + targetTable.TableName + " ends with " + matchedName + ").");
            }

            if (targetColumn.IsPrimaryKey) evidence.Add("Target column is the primary key of " + targetTable.TableName + ".");
            else if (IsUnique(targetTable, targetColumn.ColumnName)) evidence.Add("Target column has a unique index.");
            else
            {
                confidence -= 0.15;
                evidence.Add("Target column is not declared unique.");
            }

            if (NameHeuristics.TypesCompatible(sourceColumn.DataType, targetColumn.DataType))
            {
                if (!String.IsNullOrWhiteSpace(sourceColumn.DataType) && !String.IsNullOrWhiteSpace(targetColumn.DataType))
                    evidence.Add("Compatible types (" + sourceColumn.DataType + " and " + targetColumn.DataType + ").");
            }
            else
            {
                confidence -= 0.30;
                evidence.Add("Types differ (" + sourceColumn.DataType + " and " + targetColumn.DataType + "), so the join may need a cast or be wrong.");
            }

            if (sourceTable.Indexes.Any(i => i.Columns.Count > 0 && String.Equals(i.Columns[0], sourceColumn.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                confidence += 0.02;
                evidence.Add("Source column is indexed.");
            }

            if (IsView(sourceTable)) evidence.Add("Source is a view.");
            evidence.Add("No declared foreign key; confirm before relying on it.");
            return Math.Round(Math.Clamp(confidence, 0.05, 0.95), 2);
        }

        private static string? MatchEntityToken(string tableName, string entityToken, out bool suffixMatch)
        {
            suffixMatch = false;
            string normalizedTable = NameHeuristics.NormalizeIdentifier(tableName);
            if (TokenMatches(normalizedTable, entityToken)) return normalizedTable;

            List<string> words = NameHeuristics.Words(tableName);
            for (int start = 1; start < words.Count; start++)
            {
                string tail = String.Concat(words.Skip(start));
                if (tail.Length < 3) continue;
                if (TokenMatches(tail, entityToken))
                {
                    suffixMatch = true;
                    return tail;
                }
            }
            return null;
        }

        private static bool TokenMatches(string normalizedTable, string entityToken)
        {
            string singularTable = NameHeuristics.Singularize(normalizedTable);
            string normalizedToken = NameHeuristics.NormalizeIdentifier(entityToken);
            string singularToken = NameHeuristics.Singularize(normalizedToken);
            return normalizedToken == normalizedTable
                || normalizedToken == singularTable
                || singularToken == singularTable
                || normalizedTable == singularToken + "s";
        }

        private static ColumnDetail? PreferredKeyColumn(TableDetail table, string sourceColumnName)
        {
            List<ColumnDetail> pk = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            if (pk.Count == 1) return pk[0];
            if (pk.Count > 1)
                return pk.FirstOrDefault(c => String.Equals(c.ColumnName, sourceColumnName, StringComparison.OrdinalIgnoreCase));

            ColumnDetail? id = table.Columns.FirstOrDefault(c => String.Equals(c.ColumnName, "id", StringComparison.OrdinalIgnoreCase));
            if (id != null) return id;

            string singular = NameHeuristics.Singularize(NameHeuristics.NormalizeIdentifier(table.TableName));
            return table.Columns.FirstOrDefault(c => NameHeuristics.NormalizeIdentifier(c.ColumnName) == singular + "id")
                ?? table.Columns.FirstOrDefault(c => String.Equals(c.ColumnName, sourceColumnName, StringComparison.OrdinalIgnoreCase) && IsUnique(table, c.ColumnName));
        }

        private static bool IsUnique(TableDetail table, string column)
        {
            return table.Indexes.Any(i => i.IsUnique && i.Columns.Count == 1 && String.Equals(i.Columns[0], column, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsView(TableDetail table)
        {
            return table.TableType.Contains("VIEW", StringComparison.OrdinalIgnoreCase);
        }

        private static DomainIntelligence BuildDomain(SchemaDetail detail, List<RelationshipDetail> relationships, SchemaKnowledge knowledge)
        {
            DomainIntelligence domain = new DomainIntelligence();
            int declaredCount = relationships.Count(r => r.IsDeclared);
            int inferredCount = relationships.Count - declaredCount;
            int views = detail.Tables.Count(IsView);

            domain.Summary = "Crawled " + (detail.Tables.Count - views) + " table(s) and " + views + " view(s) in schema " + detail.Schema
                + " with " + declaredCount + " declared relationship(s) and " + inferredCount + " inferred relationship candidate(s).";
            domain.Summary += knowledge.HasDatabaseContext ? " Database context is saved." : " No database context is saved yet.";

            foreach (TableDetail table in detail.Tables.OrderBy(t => t.SchemaName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
            {
                ResourceKnowledgeEntry? entry = knowledge.Table(table.SchemaName, table.TableName);
                domain.Entities.Add(new DomainEntity
                {
                    SchemaName = table.SchemaName,
                    TableName = table.TableName,
                    TableType = table.TableType,
                    RowEstimate = table.RowEstimate,
                    Role = InferTableRole(table, relationships),
                    Summary = BuildEntitySummary(table, entry != null),
                    KeyColumns = SelectKeyColumns(table),
                    HasContext = entry != null,
                    ContextSource = entry?.Source.ToString()
                });
            }

            foreach (RelationshipDetail r in relationships.Take(30))
            {
                string label = Qualify(r.FromSchema, r.FromTable) + " joins to " + Qualify(r.ToSchema, r.ToTable) + " through " + r.FromColumn + " -> " + (r.ToColumn ?? "its primary key");
                if (!r.IsDeclared) label += " (inferred, confidence " + r.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + ")";
                AddDistinct(domain.Workflows, label);
            }

            foreach (TableDetail table in detail.Tables)
            {
                foreach (ColumnDetail column in table.Columns)
                {
                    string label = Qualify(table.SchemaName, table.TableName) + "." + column.ColumnName;
                    if (NameHeuristics.IsMetricColumn(column.ColumnName, column.DataType) && !column.IsPrimaryKey)
                    {
                        if (AddDistinct(domain.Metrics, label))
                        {
                            List<string> aggregations = NameHeuristics.MetricAggregations(column.ColumnName, out string? note);
                            domain.MetricDetails.Add(new MetricCandidate { Column = label, DataType = column.DataType, Aggregations = aggregations, Note = note });
                        }
                    }
                    if (IsCommonFilter(column)) AddDistinct(domain.CommonFilters, label);
                    if (NameHeuristics.IsFreshnessColumn(column.ColumnName, column.DataType)) AddDistinct(domain.FreshnessColumns, label);
                    if (NameHeuristics.IsTenantColumn(column.ColumnName)) AddDistinct(domain.TenantColumns, label);
                    if (NameHeuristics.IsSoftDeleteColumn(column.ColumnName)) AddDistinct(domain.SoftDeleteColumns, label);
                }
            }

            domain.Metrics = domain.Metrics.Take(40).ToList();
            domain.MetricDetails = domain.MetricDetails.Take(40).ToList();
            domain.CommonFilters = domain.CommonFilters.Take(40).ToList();
            domain.FreshnessColumns = domain.FreshnessColumns.Take(40).ToList();
            domain.TenantColumns = domain.TenantColumns.Take(40).ToList();
            domain.SoftDeleteColumns = domain.SoftDeleteColumns.Take(40).ToList();
            return domain;
        }

        private static bool IsCommonFilter(ColumnDetail column)
        {
            return NameHeuristics.IsStatusColumn(column.ColumnName)
                || NameHeuristics.IsTypeColumn(column.ColumnName)
                || NameHeuristics.IsActivityColumn(column.ColumnName)
                || NameHeuristics.IsTenantColumn(column.ColumnName)
                || NameHeuristics.IsSoftDeleteColumn(column.ColumnName)
                || NameHeuristics.IsFreshnessColumn(column.ColumnName, column.DataType);
        }

        private static List<AmbiguitySignal> BuildAmbiguities(SchemaDetail detail, SchemaKnowledge knowledge, string? question)
        {
            bool promptSpecific = question != null;
            List<AmbiguitySignal> signals = new List<AmbiguitySignal>();
            List<string> context = knowledge.AllContent();

            List<AmbiguityCandidate> freshness = ColumnCandidates(detail, c => NameHeuristics.IsFreshnessColumn(c.ColumnName, c.DataType));
            List<AmbiguityCandidate> activity = ColumnCandidates(detail, c => NameHeuristics.IsActivityColumn(c.ColumnName) || NameHeuristics.IsSoftDeleteColumn(c.ColumnName));
            List<AmbiguityCandidate> statuses = ColumnCandidates(detail, c => NameHeuristics.IsStatusColumn(c.ColumnName));
            List<AmbiguityCandidate> metrics = ColumnCandidates(detail, c => NameHeuristics.IsMetricColumn(c.ColumnName, c.DataType) && !c.IsPrimaryKey);
            List<AmbiguityCandidate> owners = ColumnCandidates(detail, c => NameHeuristics.IsOwnerColumn(c.ColumnName));
            List<AmbiguityCandidate> parties = detail.Tables
                .Where(t => !IsView(t) && NameHeuristics.IsPartyTable(t.TableName))
                .Select(t => new AmbiguityCandidate(t.SchemaName, t.TableName, null))
                .ToList();

            bool anyTableMentioned = question != null && detail.Tables.Any(t => MentionsTable(question, t.TableName));
            AddAmbiguity(signals, question, context, anyTableMentioned, "latest", new[] { "latest", "recent", "newest", "most recent" }, new[] { "last" }, freshness, "Which timestamp defines latest?", "Several timestamp columns could define the latest records.");
            AddAmbiguity(signals, question, context, anyTableMentioned, "active", new[] { "active", "inactive", "enabled" }, Array.Empty<string>(), activity, "Which column or rule defines active records?", "Several activity, status, or soft-delete columns could define active records.");
            AddAmbiguity(signals, question, context, anyTableMentioned, "status", new[] { "status", "state" }, Array.Empty<string>(), statuses, "Which status column should be used?", "Several status-like columns exist.");
            AddAmbiguity(signals, question, context, anyTableMentioned, "revenue", new[] { "revenue", "sales", "income" }, new[] { "total", "amount" }, metrics, "Which metric column defines revenue or total value, and which rows count (for example excluding cancelled orders)?", "Several numeric amount-like columns exist.");
            AddAmbiguity(signals, question, context, anyTableMentioned, "owner", new[] { "owner", "owned", "assignee" }, Array.Empty<string>(), owners, "Which ownership column should be used?", "Several owner-like columns exist.");
            AddAmbiguity(signals, question, context, anyTableMentioned, "customer", new[] { "customer", "client", "account", "user" }, Array.Empty<string>(), parties, "Which table represents the customer (or user, client, account)?", "Several person, customer, user, or account tables exist.");

            if (!promptSpecific && !knowledge.HasDatabaseContext && detail.Tables.Count > 0)
            {
                signals.Add(new AmbiguitySignal
                {
                    Term = "business_context",
                    Reason = "No database-level context is saved.",
                    Question = "What business definitions should agents use for this database?",
                    Candidates = new List<string> { "Generate database context (sql_generate_context)", "Write database context (sql_update_context)", "Add table context for core tables" }
                });
            }

            return signals.Take(12).ToList();
        }

        private static void AddAmbiguity(
            List<AmbiguitySignal> signals,
            string? question,
            List<string> context,
            bool anyTableMentioned,
            string term,
            string[] strongTriggers,
            string[] weakTriggers,
            List<AmbiguityCandidate> candidates,
            string questionText,
            string reason)
        {
            if (candidates.Count <= 1) return;
            string[] triggers = strongTriggers.Concat(weakTriggers).ToArray();
            if (DefinedInContext(context, term, triggers)) return;

            List<AmbiguityCandidate> remaining = candidates;
            if (question != null)
            {
                List<AmbiguityCandidate> byTable = remaining.Where(c => MentionsTable(question, c.Table)).ToList();
                bool strong = strongTriggers.Any(t => ContainsPhrase(question, t));
                bool weak = weakTriggers.Any(t => ContainsPhrase(question, t))
                    && !IsCountQuestion(question)
                    && (!anyTableMentioned || byTable.Count > 0);
                if (!strong && !weak) return;

                if (byTable.Count == 1) return;
                if (byTable.Count > 1) remaining = byTable;

                List<AmbiguityCandidate> byColumn = remaining
                    .Where(c => c.Column != null && !triggers.Contains(NameHeuristics.NormalizeIdentifier(c.Column)) && MentionsIdentifier(question, c.Column))
                    .ToList();
                if (byColumn.Count == 1) return;
                if (byColumn.Count > 1) remaining = byColumn;

                if (remaining.Count <= 1) return;
            }

            signals.Add(new AmbiguitySignal
            {
                Term = term,
                Reason = reason,
                Question = questionText,
                Candidates = remaining.Select(c => c.Label).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList()
            });
        }

        private static bool IsCountQuestion(string question)
        {
            return ContainsPhrase(question, "how many") || ContainsPhrase(question, "number of") || ContainsPhrase(question, "count");
        }

        private static bool ContainsPhrase(string question, string phrase)
        {
            string text = " " + String.Join(" ", NameHeuristics.Words(question)) + " ";
            string p = String.Join(" ", NameHeuristics.Words(phrase));
            return text.Contains(" " + p + " ", StringComparison.Ordinal) || text.Contains(" " + p + "s ", StringComparison.Ordinal);
        }

        private static bool DefinedInContext(List<string> context, string term, string[] triggers)
        {
            if (context.Count == 0) return false;
            IEnumerable<string> words = triggers.Append(term).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string word in words)
            {
                string escaped = Regex.Escape(word);
                Regex definition = new Regex(
                    "\\b" + escaped + "s?\\b[^.\\n]{0,60}?\\b(means|mean|is defined as|are defined as|defined as|refers to|refer to|is computed|is calculated|equals)\\b"
                    + "|\\bdefin\\w*\\s+(of\\s+)?[\"'`*]*" + escaped + "\\b"
                    + "|\\b" + escaped + "s?[\"'`*]*\\s*(:|=)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (context.Any(c => definition.IsMatch(c))) return true;
            }
            return false;
        }

        private static List<AmbiguityCandidate> ColumnCandidates(SchemaDetail detail, Func<ColumnDetail, bool> predicate)
        {
            List<AmbiguityCandidate> candidates = new List<AmbiguityCandidate>();
            foreach (TableDetail table in detail.Tables)
            {
                foreach (ColumnDetail column in table.Columns)
                {
                    if (predicate(column)) candidates.Add(new AmbiguityCandidate(table.SchemaName, table.TableName, column.ColumnName));
                }
            }
            return candidates;
        }

        private static ContextQualityScore BuildContextQuality(SchemaDetail detail, List<RelationshipDetail> relationships, List<AmbiguitySignal> ambiguities, SchemaKnowledge knowledge)
        {
            ContextQualityScore quality = new ContextQualityScore();
            int tableCount = detail.Tables.Count;
            List<ResourceKnowledgeEntry> tableEntries = detail.Tables
                .Select(t => knowledge.Table(t.SchemaName, t.TableName))
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();
            int declared = relationships.Count(r => r.IsDeclared);
            int inferred = relationships.Count - declared;

            quality.TotalTables = tableCount;
            quality.TablesWithContext = tableEntries.Count;
            quality.TablesWithGeneratedContext = tableEntries.Count(e => e.Source == KnowledgeSourceEnum.Generated);
            quality.HasDatabaseContext = knowledge.HasDatabaseContext;
            quality.DeclaredRelationships = declared;
            quality.InferredRelationships = inferred;

            int score = 20;
            if (tableCount == 0) AddSignal(quality, "crawl", "blocker", "The crawl found no tables or views in schema " + detail.Schema + ".", "Check the schema name with sql_list_schemas.");

            if (knowledge.HasDatabaseContext) score += 20;
            else AddSignal(quality, "database_context", "warning", "Database-level context is missing.", "Generate it with sql_generate_context or write it with sql_update_context: purpose, major entities, business definitions, and caveats.");

            if (tableCount > 0)
            {
                int coverage = (int)Math.Round((double)tableEntries.Count / tableCount * 30.0);
                score += coverage;
                if (coverage < 20)
                    AddSignal(quality, "table_context", "warning", "Only " + tableEntries.Count + " of " + tableCount + " table(s) have saved context.", "Build table context for core tables first.");
            }

            score += Math.Min(20, declared * 4 + inferred * 2);
            if (declared == 0 && inferred > 0)
                AddSignal(quality, "relationships", "warning", "Only inferred relationship candidates are available.", "Review inferred relationships and record confirmed join paths in context.");
            if (relationships.Count == 0 && tableCount > 1)
                AddSignal(quality, "relationships", "warning", "No relationship evidence was found.", "Add join guidance to database or table context.");

            List<AmbiguitySignal> termSignals = ambiguities.Where(a => a.Term != "business_context").ToList();
            if (termSignals.Count > 0)
            {
                score -= Math.Min(20, termSignals.Count * 4);
                AddSignal(quality, "ambiguity", "warning", termSignals.Count + " ambiguous term(s) may need clarification: " + String.Join(", ", termSignals.Select(a => a.Term)) + ".", "Define them in database or table context (for example \"revenue means ...\").");
            }

            if (quality.TablesWithGeneratedContext > 0)
                AddSignal(quality, "generated_context", "info", quality.TablesWithGeneratedContext + " table context(s) were generated by a model and not yet edited by a person.", "Have an operator review generated context in the dashboard.");

            quality.Score = Math.Clamp(score, 0, 100);
            if (quality.Score >= 85) quality.Label = "Strong";
            else if (quality.Score >= 65) quality.Label = "Good";
            else if (quality.Score >= 40) quality.Label = "Needs context";
            else quality.Label = "Degraded";
            return quality;
        }

        private static List<string> BuildSuggestedQuestions(DomainIntelligence domain)
        {
            List<string> questions = new List<string>();
            foreach (DomainEntity entity in domain.Entities.Where(e => e.Role != "view").Take(4))
                questions.Add("How many records are in " + Qualify(entity.SchemaName, entity.TableName) + "?");
            HashSet<string> views = new HashSet<string>(domain.Entities.Where(e => e.Role == "view").Select(e => Qualify(e.SchemaName, e.TableName) + "."), StringComparer.OrdinalIgnoreCase);
            foreach (string metric in domain.Metrics.Where(m => !views.Any(v => m.StartsWith(v, StringComparison.OrdinalIgnoreCase))).Take(3))
                questions.Add("What is the total " + metric + " by month?");
            foreach (string freshness in domain.FreshnessColumns.Take(3))
                questions.Add("What are the latest records by " + freshness + "?");
            return questions.Take(8).ToList();
        }

        private static string InferTableRole(TableDetail table, List<RelationshipDetail> relationships)
        {
            if (IsView(table)) return "view";
            int outbound = relationships.Count(r => SameTable(r.FromSchema, r.FromTable, table));
            int inbound = relationships.Count(r => SameTable(r.ToSchema, r.ToTable, table));

            if (outbound >= 2 && inbound == 0 && table.Columns.Count <= outbound + 4) return "junction";
            if (NameHeuristics.HasWord(table.TableName, "log", "event", "history", "audit", "transaction")) return "event";
            if (NameHeuristics.HasWord(table.TableName, "type", "status", "category", "lookup")) return "lookup";
            if (inbound > 0 && outbound == 0) return "core entity";
            return "entity";
        }

        private static bool SameTable(string? schema, string? table, TableDetail candidate)
        {
            return String.Equals(table, candidate.TableName, StringComparison.OrdinalIgnoreCase)
                && (String.IsNullOrWhiteSpace(schema) || String.Equals(schema, candidate.SchemaName, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildEntitySummary(TableDetail table, bool hasContext)
        {
            List<string> parts = new List<string>();
            if (IsView(table)) parts.Add("a view");
            parts.Add(table.Columns.Count + " column(s)");
            if (table.RowEstimate.HasValue) parts.Add("about " + table.RowEstimate.Value.ToString(CultureInfo.InvariantCulture) + " row(s)");
            if (table.ForeignKeys.Count > 0) parts.Add(table.ForeignKeys.Count + " declared FK(s)");
            if (hasContext) parts.Add("saved context");
            return "Contains " + String.Join(", ", parts) + ".";
        }

        private static List<string> SelectKeyColumns(TableDetail table)
        {
            List<string> columns = new List<string>();
            foreach (ColumnDetail column in table.Columns)
            {
                if (column.IsPrimaryKey || NameHeuristics.IsLikelyForeignKey(column.ColumnName) || IsCommonFilter(column) || NameHeuristics.IsMetricColumn(column.ColumnName, column.DataType))
                    AddDistinct(columns, column.ColumnName);
            }
            if (columns.Count == 0)
            {
                foreach (ColumnDetail column in table.Columns.Take(6)) AddDistinct(columns, column.ColumnName);
            }
            return columns.Take(10).ToList();
        }

        private static IEnumerable<string> PhraseVariants(List<string> words)
        {
            if (words.Count == 0) yield break;
            string head = words.Count > 1 ? String.Join(" ", words.Take(words.Count - 1)) + " " : String.Empty;
            string last = words[words.Count - 1];
            string singular = NameHeuristics.Singularize(last);
            yield return head + last;
            if (singular != last) yield return head + singular;
            yield return head + singular + "s";
            if (singular.EndsWith("y", StringComparison.Ordinal) && singular.Length > 1) yield return head + singular.Substring(0, singular.Length - 1) + "ies";
            if (words.Count > 1) yield return String.Concat(words);
        }

        private static void AddSignal(ContextQualityScore quality, string key, string severity, string message, string recommendation)
        {
            quality.Signals.Add(new ContextQualitySignal { Key = key, Severity = severity, Message = message, Recommendation = recommendation });
        }

        private static string RelationshipKey(RelationshipDetail r)
        {
            return NameHeuristics.NormalizeIdentifier(r.FromSchema) + "." + NameHeuristics.NormalizeIdentifier(r.FromTable) + "." + NameHeuristics.NormalizeIdentifier(r.FromColumn)
                + "->" + NameHeuristics.NormalizeIdentifier(r.ToSchema) + "." + NameHeuristics.NormalizeIdentifier(r.ToTable) + "." + NameHeuristics.NormalizeIdentifier(r.ToColumn);
        }

        private static bool AddDistinct(List<string> values, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            if (values.Any(existing => String.Equals(existing, value, StringComparison.OrdinalIgnoreCase))) return false;
            values.Add(value);
            return true;
        }

        #endregion
    }
}
