namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Intelligence;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// <c>sql_generate_context</c>: draft durable database and table context with the calling profile's model from
    /// structure, statistics, and a small masked row sample (Tablix Build Context and table context generation). Rows
    /// appear only inside the prompt and are never persisted. Operator- and agent-authored entries are kept unless
    /// overwrite is true. Reads the target only; writes Hugo's knowledge (UpdatesKnowledge). Thread-safe.
    /// </summary>
    public class SqlGenerateContextTool : SqlToolBase
    {
        #region Public-Members

        /// <summary>
        /// Guard applied to generated content before it is stored. Never null.
        /// </summary>
        public KnowledgeContentGuard Guard
        {
            get
            {
                return _Guard;
            }
            set
            {
                _Guard = value ?? new KnowledgeContentGuard();
            }
        }

        /// <summary>
        /// Sample column selection and masking. Never null.
        /// </summary>
        public SampleMasker Masker
        {
            get
            {
                return _Masker;
            }
            set
            {
                _Masker = value ?? new SampleMasker();
            }
        }

        /// <summary>
        /// Maximum tables described in the database context prompt. Minimum 1, maximum 5000, default 200.
        /// </summary>
        public int MaxPromptTables
        {
            get
            {
                return _MaxPromptTables;
            }
            set
            {
                _MaxPromptTables = Math.Clamp(value, 1, 5000);
            }
        }

        /// <summary>
        /// Maximum output tokens requested for database context. Minimum 64, maximum 32768, default 2000.
        /// </summary>
        public int DatabaseMaxOutputTokens
        {
            get
            {
                return _DatabaseMaxOutputTokens;
            }
            set
            {
                _DatabaseMaxOutputTokens = Math.Clamp(value, 64, 32768);
            }
        }

        /// <summary>
        /// Maximum output tokens requested for each table context. Minimum 64, maximum 32768, default 1200.
        /// </summary>
        public int TableMaxOutputTokens
        {
            get
            {
                return _TableMaxOutputTokens;
            }
            set
            {
                _TableMaxOutputTokens = Math.Clamp(value, 64, 32768);
            }
        }

        #endregion

        #region Private-Members

        private KnowledgeContentGuard _Guard = new KnowledgeContentGuard();
        private SampleMasker _Masker = new SampleMasker();
        private int _MaxPromptTables = 200;
        private int _DatabaseMaxOutputTokens = 2000;
        private int _TableMaxOutputTokens = 1200;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlGenerateContextTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            if (context.Model == null)
                return ToolResult.Error(ConnectorErrorCodes.NotSupported, "sql_generate_context needs a language model, and this profile has no inference endpoint. Write context with sql_update_context instead.");
            if (!context.AllowKnowledgeUpdates)
                return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "This binding does not allow knowledge updates, so context cannot be generated.");

            string scope = (arguments.GetString("scope") ?? "all").Trim().ToLowerInvariant();
            if (scope != "all" && scope != "database" && scope != "tables")
                return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "scope must be all, database, or tables.");
            List<string> requested = arguments.GetStringList("tables").Where(t => !String.IsNullOrWhiteSpace(t)).ToList();
            bool overwrite = arguments.GetBool("overwrite", false);
            bool includeSamples = arguments.GetBool("includeSamples", true);
            int sampleRows = arguments.GetInt("sampleRows", 3, 0, 10);
            int maxTables = arguments.GetInt("maxTables", 50, 1, 500);
            int concurrency = arguments.GetInt("concurrency", 4, 1, 16);
            string? instructions = arguments.GetString("instructions");
            bool doDatabase = scope == "all" || scope == "database";
            bool doTables = scope == "all" || scope == "tables";

            CrawlSnapshot? snapshot = null;
            SchemaKnowledge knowledge = new SchemaKnowledge();
            List<TableDetail> selected = new List<TableDetail>();
            int remaining = 0;
            Dictionary<string, SqlQueryResult> samples = new Dictionary<string, SqlQueryResult>(StringComparer.OrdinalIgnoreCase);

            ToolResult phase = await RunAsync(context, true, async (session, timeoutSeconds, ct) =>
            {
                snapshot = await CrawlAsync(context, session, arguments.GetString("schema"), arguments.GetBool("refresh", false), ct).ConfigureAwait(false);
                knowledge = await LoadKnowledgeAsync(context, ct).ConfigureAwait(false);
                if (doTables)
                {
                    if (requested.Count > 0)
                    {
                        List<string> missing = new List<string>();
                        foreach (string name in requested)
                        {
                            List<TableDetail> matches = TableIdentity.Find(snapshot.Detail, name);
                            if (matches.Count == 0) missing.Add(name);
                            foreach (TableDetail t in matches) if (!selected.Contains(t)) selected.Add(t);
                        }
                        if (missing.Count > 0)
                            return ToolResult.Error(ConnectorErrorCodes.NotFound, "Table(s) not found in schema '" + snapshot.Detail.Schema + "': " + String.Join(", ", missing) + ". Use sql_list_tables to see what exists.");
                    }
                    else
                    {
                        SchemaKnowledge k = knowledge;
                        selected = snapshot.Detail.Tables
                            .OrderBy(t => k.Table(t.SchemaName, t.TableName) == null ? 0 : 1)
                            .ThenBy(t => t.SchemaName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                    remaining = Math.Max(0, selected.Count - maxTables);
                    selected = selected.Take(maxTables).ToList();

                    if (includeSamples && sampleRows > 0)
                    {
                        foreach (TableDetail t in selected)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (Protected(knowledge.Table(t.SchemaName, t.TableName), overwrite)) continue;
                            List<string> columns = _Masker.Columns(t);
                            if (columns.Count == 0) continue;
                            try
                            {
                                SqlQueryResult sample = await SqlExecutor.ExecuteAsync(session, Dialect.BuildSampleSql(t.SchemaName, t.TableName, columns, sampleRows), null, sampleRows, timeoutSeconds, ct).ConfigureAwait(false);
                                _Masker.Mask(sample);
                                samples[TableIdentity.Key(t)] = sample;
                            }
                            catch (DbException e)
                            {
                                context.Resource.Logger.Debug("sql_generate_context sample of " + TableIdentity.Key(t) + " failed: " + e.Message);
                            }
                        }
                    }
                }
                return ToolResult.FromText("ok");
            }, token).ConfigureAwait(false);
            if (phase.IsError || snapshot == null) return phase;

            IConnectorModel model = context.Model;
            SchemaDetail detail = snapshot.Detail;
            ContextGenerationResult result = new ContextGenerationResult { Schema = detail.Schema, Remaining = remaining };
            DatabaseIntelligence intelligence = DatabaseIntelligenceBuilder.Build(detail, knowledge);
            List<string> secrets = context.Resource.Secrets.AllValues();

            if (doDatabase)
            {
                ContextGenerationItem item = new ContextGenerationItem { Scope = SqlKnowledgeScopes.Database, Key = String.Empty };
                if (Protected(knowledge.Database, overwrite))
                {
                    item.Status = "skipped";
                    item.Reason = "Database context was written by " + (knowledge.Database!.Source == KnowledgeSourceEnum.Operator ? "an operator" : "an agent") + "; pass overwrite=true to replace it.";
                }
                else
                {
                    string prompt = SqlPrompts.BuildDatabaseContextPrompt(Dialect, detail, intelligence, knowledge, instructions, _MaxPromptTables);
                    ResourceKnowledgeEntry? stored = await GenerateAsync(context, model, item, SqlKnowledgeScopes.Database, String.Empty, knowledge.Database?.Version ?? 0, SqlPrompts.PurposeDatabaseContext, SqlPrompts.DatabaseContextSystemPrompt, prompt, _DatabaseMaxOutputTokens, secrets, result, token).ConfigureAwait(false);
                    if (stored != null) knowledge.Database = stored;
                }
                result.Items.Add(item);
            }

            if (doTables && selected.Count > 0)
            {
                ContextGenerationItem[] items = new ContextGenerationItem[selected.Count];
                using (SemaphoreSlim gate = new SemaphoreSlim(concurrency, concurrency))
                {
                    List<Task> tasks = new List<Task>();
                    for (int i = 0; i < selected.Count; i++)
                    {
                        int index = i;
                        TableDetail table = selected[index];
                        string key = TableIdentity.Key(table);
                        ContextGenerationItem item = new ContextGenerationItem { Scope = SqlKnowledgeScopes.Table, Key = key };
                        items[index] = item;
                        ResourceKnowledgeEntry? existing = knowledge.Table(table.SchemaName, table.TableName);
                        if (Protected(existing, overwrite))
                        {
                            item.Status = "skipped";
                            item.Reason = "Table context was written by " + (existing!.Source == KnowledgeSourceEnum.Operator ? "an operator" : "an agent") + "; pass overwrite=true to replace it.";
                            continue;
                        }
                        samples.TryGetValue(key, out SqlQueryResult? sample);
                        item.UsedSample = sample != null;
                        string prompt = SqlPrompts.BuildTableContextPrompt(Dialect, detail, table, intelligence, knowledge, sample, instructions);
                        tasks.Add(Task.Run(async () =>
                        {
                            await gate.WaitAsync(token).ConfigureAwait(false);
                            try
                            {
                                await GenerateAsync(context, model, item, SqlKnowledgeScopes.Table, key, existing?.Version ?? 0, SqlPrompts.PurposeTableContext, SqlPrompts.TableContextSystemPrompt, prompt, _TableMaxOutputTokens, secrets, result, token).ConfigureAwait(false);
                            }
                            finally
                            {
                                gate.Release();
                            }
                        }, token));
                    }
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                result.Items.AddRange(items);
            }

            result.Generated = result.Items.Count(i => i.Status == "generated");
            result.Skipped = result.Items.Count(i => i.Status == "skipped");
            result.Failed = result.Items.Count(i => i.Status == "failed");

            StringBuilder sb = new StringBuilder();
            sb.Append("Context generation for schema ").Append(detail.Schema).Append(": ").Append(result.Generated).Append(" generated, ").Append(result.Skipped).Append(" skipped, ").Append(result.Failed).Append(" failed");
            if (result.Model != null) sb.Append(" (model ").Append(result.Model).Append(')');
            sb.Append(".\n");
            foreach (ContextGenerationItem i in result.Items)
            {
                sb.Append("- ").Append(i.Scope == SqlKnowledgeScopes.Database ? "database" : i.Key).Append(": ").Append(i.Status);
                if (i.Status == "generated") sb.Append(" (version ").Append(i.Version.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(i.Chars).Append(" chars").Append(i.UsedSample ? ", sampled" : String.Empty).Append(')');
                if (i.Reason != null) sb.Append(". ").Append(i.Reason);
                sb.Append('\n');
            }
            if (remaining > 0) sb.Append(remaining).Append(" more table(s) were not processed (maxTables ").Append(maxTables).Append("); call again to continue.\n");
            sb.Append("Generated context is stored with source Generated for operator review; read it with sql_get_context.\n");

            ToolResult output = BoundedPayload(context, sb.ToString(), result, result.Items.Count);
            output.Metrics["generated"] = result.Generated;
            output.Metrics["failed"] = result.Failed;
            output.Metrics["skipped"] = result.Skipped;
            if (result.Generated == 0 && result.Failed > 0)
            {
                output.IsError = true;
                output.ErrorCode = ConnectorErrorCodes.UpstreamError;
            }
            return output;
        }

        #endregion

        #region Private-Methods

        private static bool Protected(ResourceKnowledgeEntry? entry, bool overwrite)
        {
            if (overwrite || entry == null || String.IsNullOrWhiteSpace(entry.Content)) return false;
            return entry.Source == KnowledgeSourceEnum.Operator || entry.Source == KnowledgeSourceEnum.Agent;
        }

        private async Task<ResourceKnowledgeEntry?> GenerateAsync(
            ConnectorInvocationContext context,
            IConnectorModel model,
            ContextGenerationItem item,
            string scope,
            string key,
            int expectedVersion,
            string purpose,
            string systemPrompt,
            string prompt,
            int maxTokens,
            List<string> secrets,
            ContextGenerationResult result,
            CancellationToken token)
        {
            try
            {
                ConnectorModelResponse response = await model.CompleteAsync(new ConnectorModelRequest
                {
                    Purpose = purpose,
                    SystemPrompt = systemPrompt,
                    UserPrompt = prompt,
                    MaxOutputTokens = maxTokens,
                    Temperature = 0
                }, token).ConfigureAwait(false);

                string content = ModelText.NormalizeGeneratedContext(response.Text);
                if (content.Length == 0)
                {
                    item.Status = "failed";
                    item.Reason = "The model returned empty context; nothing was saved.";
                    return null;
                }
                string? refusal = _Guard.Check(content, secrets);
                if (refusal != null)
                {
                    item.Status = "failed";
                    item.Reason = "Generated context was not saved: " + refusal;
                    return null;
                }

                if (!String.IsNullOrWhiteSpace(response.Model))
                {
                    lock (result) result.Model = response.Model;
                }
                ResourceKnowledgeEntry update = new ResourceKnowledgeEntry
                {
                    Scope = scope,
                    Key = key,
                    Content = content,
                    Source = KnowledgeSourceEnum.Generated,
                    UpdatedBy = String.IsNullOrWhiteSpace(response.Model) ? Actor(context, "model") : "model " + response.Model
                };
                IResourceKnowledgeStore store = context.Resource.Knowledge;
                ResourceKnowledgeEntry? stored = store is IVersionedResourceKnowledgeStore versioned
                    ? await versioned.TryPutAsync(update, expectedVersion, token).ConfigureAwait(false)
                    : await store.PutAsync(update, token).ConfigureAwait(false);
                if (stored == null)
                {
                    item.Status = "failed";
                    item.Reason = "version_conflict: Context changed during generation; read it again before retrying.";
                    return null;
                }
                item.Status = "generated";
                item.Chars = content.Length;
                item.Version = stored.Version;
                item.Content = content;
                return stored;
            }
            catch (ConnectorException e)
            {
                item.Status = "failed";
                item.Reason = "Model call failed (" + e.Code + "): " + e.Message;
                return null;
            }
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_generate_context",
                Title = "Generate durable context",
                Description = "Draft durable database and table context with the profile's model from the crawled structure, relationships, statistics, and a small masked row sample (sample rows go only into the prompt and are never stored). "
                    + "scope all (default), database, or tables; tables limits which tables. Operator- and agent-authored entries are kept unless overwrite=true; model-generated entries are refreshed. "
                    + "Tables without context go first, up to maxTables per call, with bounded concurrency. Results are stored with source Generated for operator review. Reads the database only.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"scope\":{\"type\":\"string\",\"enum\":[\"all\",\"database\",\"tables\"],\"description\":\"What to generate. Default all.\"},"
                    + "\"tables\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Only these tables (bare or schema-qualified). Optional.\"},"
                    + "\"overwrite\":{\"type\":\"boolean\",\"description\":\"Replace operator- and agent-authored entries too. Default false.\"},"
                    + "\"includeSamples\":{\"type\":\"boolean\",\"description\":\"Show the model a few masked rows per table. Default true.\"},"
                    + "\"sampleRows\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":10,\"description\":\"Rows sampled per table. Default 3.\"},"
                    + "\"maxTables\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":500,\"description\":\"Tables processed per call. Default 50.\"},"
                    + "\"concurrency\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":16,\"description\":\"Parallel model calls. Default 4.\"},"
                    + "\"instructions\":{\"type\":\"string\",\"description\":\"Extra guidance for the model (for example business terms to define). Optional.\"},"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Defaults to the resource's default schema.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl first. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                UpdatesKnowledge = true,
                RequiresModel = true,
                OutputDescription = "Per-entry outcome lines; structured content { schema, model, generated, skipped, failed, remaining, items: [{ scope, key, status, reason, chars, version, usedSample, content }] }."
            };
        }

        #endregion
    }
}
