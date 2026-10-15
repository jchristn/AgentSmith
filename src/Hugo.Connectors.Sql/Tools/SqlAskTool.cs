// The flow is adapted from Tablix (https://github.com/jchristn/Tablix), commit
// 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6: ChatHandler.PrepareChatAsync (prompt ambiguity detection),
// ExecuteFallbackPlanningAsync (JSON planning with a reformat retry), ExecutePlannedQueryAsync, BuildVerifiedAnswer,
// and ChatQueryExecutionService (retry after a schema refresh when the failure looks schema-related). Copyright (c)
// 2026 Joel Christner and Adam Wingate, MIT License. Modified for Hugo: one tool call instead of a chat loop; an
// ambiguous term stops before planning unless the caller passes assumptions; relevant tables are selected first on
// large schemas; generated SQL must classify as a single Read through StatementClassifier and runs in a read-only
// session with the resource's limits; every failure is retried once with the database error; the answer is written
// only from the returned rows. See Vendored/VENDORED.md.
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
    /// <c>sql_ask</c>: answer a plain-language question with guarded, verified SQL. Detects ambiguous terms and asks
    /// for clarification, plans one read-only statement from durable context, intelligence, and relationships,
    /// validates it with <see cref="StatementClassifier"/>, runs it with the resource's limits, retries once on error,
    /// and returns a verification envelope (Verified, Partial, Blocked, Ambiguous). Thread-safe.
    /// </summary>
    public class SqlAskTool : SqlToolBase
    {
        #region Public-Members

        /// <summary>
        /// Schemas with more tables than this get a table selection step before planning. Minimum 1, maximum 10000,
        /// default 30.
        /// </summary>
        public int FullSchemaTableLimit
        {
            get
            {
                return _FullSchemaTableLimit;
            }
            set
            {
                _FullSchemaTableLimit = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Maximum tables given to the planner after selection. Minimum 1, maximum 200, default 12.
        /// </summary>
        public int MaxSelectedTables
        {
            get
            {
                return _MaxSelectedTables;
            }
            set
            {
                _MaxSelectedTables = Math.Clamp(value, 1, 200);
            }
        }

        /// <summary>
        /// Maximum tables listed in the selection prompt. Minimum 10, maximum 5000, default 400.
        /// </summary>
        public int MaxIndexTables
        {
            get
            {
                return _MaxIndexTables;
            }
            set
            {
                _MaxIndexTables = Math.Clamp(value, 10, 5000);
            }
        }

        /// <summary>
        /// Maximum result rows shown to the model when writing the answer. Minimum 1, maximum 1000, default 50.
        /// </summary>
        public int AnswerRows
        {
            get
            {
                return _AnswerRows;
            }
            set
            {
                _AnswerRows = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Maximum output tokens for planning. Minimum 64, maximum 32768, default 1500.
        /// </summary>
        public int PlanMaxOutputTokens
        {
            get
            {
                return _PlanMaxOutputTokens;
            }
            set
            {
                _PlanMaxOutputTokens = Math.Clamp(value, 64, 32768);
            }
        }

        /// <summary>
        /// Maximum output tokens for the answer. Minimum 64, maximum 32768, default 800.
        /// </summary>
        public int AnswerMaxOutputTokens
        {
            get
            {
                return _AnswerMaxOutputTokens;
            }
            set
            {
                _AnswerMaxOutputTokens = Math.Clamp(value, 64, 32768);
            }
        }

        #endregion

        #region Private-Members

        private int _FullSchemaTableLimit = 30;
        private int _MaxSelectedTables = 12;
        private int _MaxIndexTables = 400;
        private int _AnswerRows = 50;
        private int _PlanMaxOutputTokens = 1500;
        private int _AnswerMaxOutputTokens = 800;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlAskTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            string question = (arguments.GetString("question") ?? String.Empty).Trim();
            if (question.Length == 0) return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "Argument 'question' is required.");
            if (context.Model == null)
                return ToolResult.Error(ConnectorErrorCodes.NotSupported, "sql_ask needs a language model, and this profile has no inference endpoint. Use sql_get_agent_pack, sql_describe_table, and sql_query instead.");
            IConnectorModel model = context.Model;

            List<string> assumptions = arguments.GetStringList("assumptions").Where(a => !String.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
            string? clarification = arguments.GetString("clarification");
            if (!String.IsNullOrWhiteSpace(clarification)) assumptions.Add(clarification.Trim());
            List<string> hints = arguments.GetStringList("tables").Where(t => !String.IsNullOrWhiteSpace(t)).ToList();
            int maxRows = EffectiveMaxRows(context, arguments);
            int evidenceRows = arguments.GetInt("evidenceRows", 10, 0, 50);
            string? schemaArgument = arguments.GetString("schema");

            CrawlSnapshot? snapshot = arguments.GetBool("refresh", false) ? null : TryCachedCrawl(context, schemaArgument);
            if (snapshot == null)
            {
                bool refresh = arguments.GetBool("refresh", false);
                ToolResult crawl = await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
                {
                    snapshot = await CrawlAsync(context, session, schemaArgument, refresh, ct).ConfigureAwait(false);
                    return ToolResult.FromText("ok");
                }, token).ConfigureAwait(false);
                if (crawl.IsError || snapshot == null) return crawl;
            }

            SchemaKnowledge knowledge = await LoadKnowledgeAsync(context, token).ConfigureAwait(false);
            VerifiedAnswer answer = new VerifiedAnswer { Question = question, Assumptions = new List<string>(assumptions) };

            List<AmbiguitySignal> ambiguities = DatabaseIntelligenceBuilder.FindPromptAmbiguities(snapshot.Detail, knowledge, question);
            answer.Ambiguities = ambiguities;
            if (ambiguities.Count > 0 && assumptions.Count == 0)
            {
                answer.Status = VerificationStatusEnum.Ambiguous;
                answer.Clarification = String.Join(" ", ambiguities.Select(a => a.Question + " Candidates: " + String.Join("; ", a.Candidates.Take(6)) + "."));
                answer.Summary = "The question uses " + String.Join(", ", ambiguities.Select(a => "\"" + a.Term + "\"")) + ", which this schema can read in more than one way. No SQL was run.";
                return Finish(context, answer);
            }

            try
            {
                return await AnswerAsync(context, model, snapshot, knowledge, answer, question, assumptions, ambiguities, hints, maxRows, evidenceRows, token).ConfigureAwait(false);
            }
            catch (ConnectorException e) when (e.Code != ConnectorErrorCodes.InvalidArgument && e.Code != ConnectorErrorCodes.NotFound)
            {
                answer.Status = VerificationStatusEnum.Blocked;
                answer.Error = "Model call failed (" + e.Code + "): " + e.Message;
                answer.Summary = "No answer: the model could not be reached, so nothing was verified.";
                return Finish(context, answer);
            }
            catch (ConnectorException e)
            {
                return ToolResult.Error(e.Code, e.Message);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<ToolResult> AnswerAsync(
            ConnectorInvocationContext context,
            IConnectorModel model,
            CrawlSnapshot snapshot,
            SchemaKnowledge knowledge,
            VerifiedAnswer answer,
            string question,
            List<string> assumptions,
            List<AmbiguitySignal> ambiguities,
            List<string> hints,
            int maxRows,
            int evidenceRows,
            CancellationToken token)
        {
            SchemaDetail detail = snapshot.Detail;
            DatabaseIntelligence intelligence = DatabaseIntelligenceBuilder.Build(detail, knowledge);
            List<TableDetail> tables = await SelectTablesAsync(model, detail, knowledge, intelligence, question, hints, answer, token).ConfigureAwait(false);
            string planPrompt = SqlPrompts.BuildPlanPrompt(Dialect, detail, tables, intelligence, knowledge, question, assumptions, ambiguities, maxRows);

            QueryPlan? plan = await PlanAsync(model, SqlPrompts.PurposePlan, planPrompt, answer, token).ConfigureAwait(false);
            if (plan == null)
                return Blocked(context, answer, "The model did not return a usable query plan.", null);
            if (plan.Assumptions != null) answer.Assumptions.AddRange(plan.Assumptions.Where(a => !String.IsNullOrWhiteSpace(a) && !answer.Assumptions.Contains(a)));
            if (plan.Tables != null) answer.Tables = plan.Tables;

            if (plan.Ambiguous)
            {
                answer.Status = VerificationStatusEnum.Ambiguous;
                answer.Clarification = String.IsNullOrWhiteSpace(plan.Clarification) ? "The question can be read more than one way; please clarify what it means." : plan.Clarification.Trim();
                answer.Summary = "The planner found the question ambiguous. No SQL was run.";
                return Finish(context, answer);
            }
            if (!plan.Execute || String.IsNullOrWhiteSpace(plan.Sql))
            {
                if (!String.IsNullOrWhiteSpace(plan.Answer))
                {
                    answer.Status = VerificationStatusEnum.Partial;
                    answer.Answer = plan.Answer.Trim();
                    answer.Summary = "Answered from schema and saved context without running SQL; no row data was verified.";
                    answer.Evidence.Add("No query was executed; the answer rests on the crawled schema and durable context.");
                    return Finish(context, answer);
                }
                return Blocked(context, answer, "The model did not produce SQL for this question.", null);
            }

            string sql = ModelText.NormalizeSql(plan.Sql);
            SqlQueryResult? result = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                answer.Sql = sql;
                StatementClassification classification = StatementClassifier.Classify(sql, Dialect.Engine, false);
                answer.Classification = classification.Effect + " (" + (classification.Rejected ? "rejected: " : String.Empty) + classification.Reason + ")";
                if (classification.Rejected || classification.Effect != EffectEnum.Read || classification.StatementCount != 1)
                    return Blocked(context, answer, "The generated SQL is not a single read-only statement (" + classification.Reason + "), so it was not run.", null);

                answer.Attempts = attempt;
                SqlQueryResult? captured = null;
                ToolResult run = await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
                {
                    captured = await SqlExecutor.ExecuteAsync(session, sql, null, maxRows, timeoutSeconds, ct).ConfigureAwait(false);
                    captured.StatementType = classification.PrimaryType;
                    return ToolResult.FromText("ok");
                }, token).ConfigureAwait(false);

                if (!run.IsError && captured != null)
                {
                    result = captured;
                    if (run.Metrics.TryGetValue("upstreamMs", out double ms)) answer.Evidence.Add("Runtime: " + ms.ToString("0.#", CultureInfo.InvariantCulture) + " ms.");
                    break;
                }

                string error = run.Text;
                answer.Evidence.Add("Attempt " + attempt + " failed (" + run.ErrorCode + "): " + error);
                if (attempt == 2 || run.ErrorCode == ConnectorErrorCodes.Timeout)
                    return Blocked(context, answer, attempt == 2 ? "The SQL failed again after one repair attempt." : "The SQL timed out.", error);

                if (IsSchemaRelatedFailure(error))
                {
                    CrawlSnapshot? fresh = null;
                    ToolResult recrawl = await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
                    {
                        fresh = await CrawlAsync(context, session, detail.Schema, true, ct).ConfigureAwait(false);
                        return ToolResult.FromText("ok");
                    }, token).ConfigureAwait(false);
                    if (!recrawl.IsError && fresh != null)
                    {
                        detail = fresh.Detail;
                        intelligence = DatabaseIntelligenceBuilder.Build(detail, knowledge);
                        List<TableDetail> refreshed = tables.SelectMany(t => TableIdentity.Find(detail, TableIdentity.Key(t))).Distinct().ToList();
                        tables = refreshed.Count > 0 ? refreshed : detail.Tables.Take(_MaxSelectedTables).ToList();
                        planPrompt = SqlPrompts.BuildPlanPrompt(Dialect, detail, tables, intelligence, knowledge, question, answer.Assumptions, ambiguities, maxRows);
                        answer.Evidence.Add("The error looked schema-related, so the schema was recrawled before the repair.");
                    }
                }

                QueryPlan? repaired = await PlanAsync(model, SqlPrompts.PurposeRetry, SqlPrompts.BuildRetryPrompt(planPrompt, sql, error), answer, token).ConfigureAwait(false);
                if (repaired == null || !repaired.Execute || String.IsNullOrWhiteSpace(repaired.Sql))
                    return Blocked(context, answer, "The SQL failed and the model did not return a repaired statement.", error);
                sql = ModelText.NormalizeSql(repaired.Sql);
            }

            if (result == null) return Blocked(context, answer, "The SQL did not run.", null);

            answer.RowCount = result.RowCount;
            answer.Truncated = result.Truncated;
            answer.Columns = result.Columns.Select(c => c.Name).ToList();
            answer.EvidenceRows = result.Rows.Take(evidenceRows).Select(r => new List<string?>(r)).ToList();
            answer.Evidence.Insert(0, "Hugo ran one read-only " + (result.StatementType ?? "SELECT") + " statement in a read-only session" + (answer.Attempts > 1 ? " (after one repair)" : String.Empty) + ".");
            answer.Evidence.Add("Rows returned: " + result.RowCount.ToString(CultureInfo.InvariantCulture) + (result.Truncated ? " (truncated at the row cap of " + result.RowLimit.ToString(CultureInfo.InvariantCulture) + "; more rows exist)." : " (complete)."));
            if (answer.Columns.Count > 0) answer.Evidence.Add("Columns: " + String.Join(", ", answer.Columns.Take(20)) + ".");
            if (answer.Tables.Count > 0) answer.Evidence.Add("Tables: " + String.Join(", ", answer.Tables) + ".");
            if (!String.IsNullOrWhiteSpace(plan.Reason)) answer.Evidence.Add("Plan: " + plan.Reason.Trim());

            string? text = null;
            try
            {
                ConnectorModelResponse response = await model.CompleteAsync(new ConnectorModelRequest
                {
                    Purpose = SqlPrompts.PurposeAnswer,
                    SystemPrompt = SqlPrompts.AnswerSystemPrompt,
                    UserPrompt = SqlPrompts.BuildAnswerPrompt(question, answer.Assumptions, sql, result, _AnswerRows),
                    MaxOutputTokens = _AnswerMaxOutputTokens,
                    Temperature = 0
                }, token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(response.Model)) answer.Model = response.Model;
                text = ModelText.NormalizeGeneratedContext(response.Text);
            }
            catch (ConnectorException e)
            {
                answer.Error = "The answer could not be written (" + e.Code + "): " + e.Message;
            }

            if (String.IsNullOrWhiteSpace(text))
            {
                answer.Status = VerificationStatusEnum.Partial;
                answer.Summary = "The query ran, but no answer text was produced; read the evidence rows directly.";
                return Finish(context, answer);
            }

            answer.Answer = text;
            if (result.Truncated)
            {
                answer.Status = VerificationStatusEnum.Partial;
                answer.Summary = "Answered from a truncated result; it covers only the first " + result.RowCount + " rows. Aggregate or filter in SQL for a complete answer.";
            }
            else
            {
                answer.Status = VerificationStatusEnum.Verified;
                answer.Summary = "Verified by running one read-only query against the database.";
            }
            return Finish(context, answer);
        }

        private async Task<List<TableDetail>> SelectTablesAsync(IConnectorModel model, SchemaDetail detail, SchemaKnowledge knowledge, DatabaseIntelligence intelligence, string question, List<string> hints, VerifiedAnswer answer, CancellationToken token)
        {
            if (hints.Count > 0)
            {
                List<TableDetail> hinted = new List<TableDetail>();
                List<string> missing = new List<string>();
                foreach (string hint in hints)
                {
                    List<TableDetail> matches = TableIdentity.Find(detail, hint);
                    if (matches.Count == 0) missing.Add(hint);
                    hinted.AddRange(matches.Where(m => !hinted.Contains(m)));
                }
                if (missing.Count > 0)
                    throw new ConnectorException(ConnectorErrorCodes.NotFound, "Table(s) not found in schema '" + detail.Schema + "': " + String.Join(", ", missing) + ". Use sql_list_tables to see what exists.");
                return WithNeighbors(detail, intelligence, hinted);
            }

            if (detail.Tables.Count <= _FullSchemaTableLimit) return detail.Tables.ToList();

            List<TableDetail> heuristic = detail.Tables
                .Select(t => new KeyValuePair<TableDetail, int>(t, Score(question, t, knowledge)))
                .Where(p => p.Value > 0)
                .OrderByDescending(p => p.Value)
                .ThenByDescending(p => p.Key.RowEstimate ?? 0)
                .Select(p => p.Key)
                .Take(_MaxSelectedTables)
                .ToList();

            ConnectorModelResponse response = await model.CompleteAsync(new ConnectorModelRequest
            {
                Purpose = SqlPrompts.PurposeSelectTables,
                SystemPrompt = SqlPrompts.SelectSystemPrompt,
                UserPrompt = SqlPrompts.BuildSelectTablesPrompt(question, detail, knowledge, _MaxSelectedTables, _MaxIndexTables),
                MaxOutputTokens = 400,
                Temperature = 0
            }, token).ConfigureAwait(false);
            TableSelection? selection = ModelText.ParseJson<TableSelection>(response.Text);
            List<TableDetail> chosen = new List<TableDetail>();
            if (selection?.Tables != null)
            {
                foreach (string name in selection.Tables)
                {
                    foreach (TableDetail t in TableIdentity.Find(detail, name))
                    {
                        if (!chosen.Contains(t) && chosen.Count < _MaxSelectedTables) chosen.Add(t);
                    }
                }
            }
            if (chosen.Count == 0)
            {
                chosen = heuristic.Count > 0 ? heuristic : detail.Tables.OrderByDescending(t => t.RowEstimate ?? 0).Take(_MaxSelectedTables).ToList();
                answer.Evidence.Add("Table selection fell back to name matching.");
            }
            else
            {
                answer.Evidence.Add("Selected " + chosen.Count + " of " + detail.Tables.Count + " tables for planning.");
            }
            return WithNeighbors(detail, intelligence, chosen);
        }

        private List<TableDetail> WithNeighbors(SchemaDetail detail, DatabaseIntelligence intelligence, List<TableDetail> tables)
        {
            List<TableDetail> result = new List<TableDetail>(tables);
            int budget = Math.Max(_MaxSelectedTables, tables.Count) + 4;
            foreach (RelationshipDetail r in intelligence.Relationships.Where(r => r.IsDeclared || r.Confidence >= 0.8))
            {
                if (result.Count >= budget) break;
                bool fromIn = tables.Any(t => Same(t, r.FromSchema, r.FromTable));
                bool toIn = tables.Any(t => Same(t, r.ToSchema, r.ToTable));
                if (fromIn == toIn) continue;
                TableDetail? other = detail.Tables.FirstOrDefault(t => fromIn ? Same(t, r.ToSchema, r.ToTable) : Same(t, r.FromSchema, r.FromTable));
                if (other != null && !result.Contains(other)) result.Add(other);
            }
            return result;
        }

        private static bool Same(TableDetail t, string? schema, string? table)
        {
            return String.Equals(t.TableName, table, StringComparison.OrdinalIgnoreCase) && (schema == null || String.Equals(t.SchemaName, schema, StringComparison.OrdinalIgnoreCase));
        }

        private static int Score(string question, TableDetail table, SchemaKnowledge knowledge)
        {
            int score = DatabaseIntelligenceBuilder.MentionsTable(question, table.TableName) ? 5 : 0;
            score += 2 * table.Columns.Count(c => DatabaseIntelligenceBuilder.MentionsIdentifier(question, c.ColumnName));
            ResourceKnowledgeEntry? entry = knowledge.Table(table.SchemaName, table.TableName);
            if (entry != null)
            {
                HashSet<string> contextWords = new HashSet<string>(NameHeuristics.Words(entry.Content).Select(NameHeuristics.Singularize));
                score += NameHeuristics.Words(question).Where(w => w.Length > 3).Select(NameHeuristics.Singularize).Distinct().Count(contextWords.Contains);
            }
            return score;
        }

        private async Task<QueryPlan?> PlanAsync(IConnectorModel model, string purpose, string prompt, VerifiedAnswer answer, CancellationToken token)
        {
            string current = prompt;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                ConnectorModelResponse response = await model.CompleteAsync(new ConnectorModelRequest
                {
                    Purpose = purpose,
                    SystemPrompt = SqlPrompts.PlanSystemPrompt,
                    UserPrompt = current,
                    MaxOutputTokens = _PlanMaxOutputTokens,
                    Temperature = 0
                }, token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(response.Model)) answer.Model = response.Model;
                QueryPlan? plan = ModelText.ParseJson<QueryPlan>(response.Text);
                if (plan != null && (plan.Ambiguous || !plan.Execute || !String.IsNullOrWhiteSpace(plan.Sql))) return plan;
                current = prompt + "\nReturn only valid JSON matching {\"execute\":true,\"sql\":\"SELECT ...\",\"tables\":[],\"reason\":\"...\"}. If execute is false, include clarification or answer; if execute is true, sql must be non-empty.";
            }
            return null;
        }

        private static bool IsSchemaRelatedFailure(string? error)
        {
            if (String.IsNullOrWhiteSpace(error)) return false;
            string n = error.ToLowerInvariant();
            return n.Contains("unknown column", StringComparison.Ordinal)
                || n.Contains("no such column", StringComparison.Ordinal)
                || n.Contains("no such table", StringComparison.Ordinal)
                || n.Contains("invalid column", StringComparison.Ordinal)
                || n.Contains("invalid object name", StringComparison.Ordinal)
                || (n.Contains("column", StringComparison.Ordinal) && n.Contains("not found", StringComparison.Ordinal))
                || (n.Contains("does not exist", StringComparison.Ordinal) && (n.Contains("column", StringComparison.Ordinal) || n.Contains("relation", StringComparison.Ordinal) || n.Contains("table", StringComparison.Ordinal)))
                || n.Contains("doesn't exist", StringComparison.Ordinal)
                || n.Contains("type mismatch", StringComparison.Ordinal)
                || n.Contains("datatype mismatch", StringComparison.Ordinal);
        }

        private static ToolResult Blocked(ConnectorInvocationContext context, VerifiedAnswer answer, string summary, string? error)
        {
            answer.Status = VerificationStatusEnum.Blocked;
            answer.Summary = summary;
            if (error != null) answer.Error = error;
            answer.Answer = null;
            return Finish(context, answer);
        }

        private static ToolResult Finish(ConnectorInvocationContext context, VerifiedAnswer answer)
        {
            ToolResult result = BoundedPayload(context, Render(answer), answer, answer.RowCount ?? 0);
            result.Metrics["status"] = (int)answer.Status;
            result.Metrics["attempts"] = answer.Attempts;
            return result;
        }

        private static string Render(VerifiedAnswer a)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Status: ").Append(a.Status).Append('\n');
            sb.Append(a.Summary).Append('\n');
            if (a.Status == VerificationStatusEnum.Ambiguous)
            {
                sb.Append("\nClarification needed: ").Append(a.Clarification).Append('\n');
                sb.Append("Ask the user, then call sql_ask again with assumptions (for example \"revenue means SUM(orders.total) excluding cancelled orders\"). Consider recording the definition with sql_update_context.\n");
                return sb.ToString();
            }
            if (a.Answer != null) sb.Append("\nAnswer: ").Append(a.Answer).Append('\n');
            if (a.Assumptions.Count > 0) sb.Append("\nAssumptions: ").Append(String.Join("; ", a.Assumptions)).Append('\n');
            if (a.Sql != null) sb.Append("\nSQL: ").Append(a.Sql).Append('\n');
            if (a.Classification != null) sb.Append("Classification: ").Append(a.Classification).Append('\n');
            if (a.RowCount.HasValue) sb.Append("Rows: ").Append(a.RowCount.Value).Append(a.Truncated ? " (truncated)" : " (complete)").Append('\n');
            if (a.Error != null) sb.Append("Error: ").Append(a.Error).Append('\n');
            if (a.Evidence.Count > 0)
            {
                sb.Append("\nEvidence:\n");
                foreach (string e in a.Evidence) sb.Append("- ").Append(e).Append('\n');
            }
            if (a.EvidenceRows.Count > 0 && a.Columns.Count > 0)
            {
                sb.Append("\nEvidence rows (first ").Append(a.EvidenceRows.Count).Append("):\n");
                sb.Append(TextTable.Render(a.Columns, a.EvidenceRows.Select(r => (IList<string?>)r).ToList(), 200));
            }
            return sb.ToString();
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_ask",
                Title = "Ask a question",
                Description = "Answer a plain-language question about the data with verified SQL. Hugo first checks for ambiguous terms (active, latest, status, revenue, owner, customer, and columns with several plausible meanings) and returns a clarification question (status Ambiguous) instead of guessing; "
                    + "after the user answers, call again with assumptions. Otherwise it plans one read-only statement from the saved context, intelligence, and relationships, refuses anything that is not a single read (Blocked), runs it with the resource's limits, retries once on error, "
                    + "and returns a verification envelope: status Verified (complete result), Partial (truncated result or schema-only answer), Blocked, or Ambiguous, with the SQL, row count, evidence rows, and an answer written only from the returned rows.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"question\":{\"type\":\"string\",\"description\":\"The question in plain language.\"},"
                    + "\"assumptions\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Definitions or choices that resolve ambiguity (for example \\\"latest means orders.ordered_at\\\"). Optional.\"},"
                    + "\"clarification\":{\"type\":\"string\",\"description\":\"The user's answer to a previous clarification question. Optional.\"},"
                    + "\"tables\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Tables to focus on (bare or schema-qualified). Optional.\"},"
                    + "\"maxRows\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100000,\"description\":\"Lower row cap for the query. Optional.\"},"
                    + "\"evidenceRows\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":50,\"description\":\"Result rows included as evidence. Default 10.\"},"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Defaults to the resource's default schema.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl first. Default false.\"}"
                    + "},\"required\":[\"question\"],\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                RequiresModel = true,
                OutputDescription = "Verification envelope as text; structured content { status, question, answer, summary, clarification, ambiguities, assumptions, sql, classification, tables, rowCount, truncated, columns, evidenceRows, evidence, error, attempts, model }."
            };
        }

        #endregion
    }
}
