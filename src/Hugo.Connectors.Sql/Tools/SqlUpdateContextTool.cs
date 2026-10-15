namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Intelligence;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// <c>sql_update_context</c>: record a durable discovery (a confirmed join path, a business definition, a correction)
    /// in the database or table context (Tablix <c>tablix_update_context</c>). Changes Hugo's knowledge, never the
    /// database, so the effect toward the target is Read; the binding must allow knowledge updates. Content with
    /// secrets, credentials, personal data, or raw rows is refused. Operator-authored entries can be appended to but not
    /// replaced. Thread-safe.
    /// </summary>
    public class SqlUpdateContextTool : SqlToolBase
    {
        #region Public-Members

        /// <summary>
        /// Guard applied to the new content and the resulting entry. Never null.
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
        /// Maximum characters accepted in one update. Minimum 50, maximum 100000, default 4000.
        /// </summary>
        public int MaxUpdateChars
        {
            get
            {
                return _MaxUpdateChars;
            }
            set
            {
                _MaxUpdateChars = Math.Clamp(value, 50, 100000);
            }
        }

        #endregion

        #region Private-Members

        private KnowledgeContentGuard _Guard = new KnowledgeContentGuard();
        private int _MaxUpdateChars = 4000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlUpdateContextTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            if (!context.AllowKnowledgeUpdates)
                return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "This binding does not allow knowledge updates, so the context was not changed. Tell the operator what you would record instead.");

            string? table = arguments.GetString("table");
            string scope = (arguments.GetString("scope") ?? (String.IsNullOrWhiteSpace(table) ? SqlKnowledgeScopes.Database : SqlKnowledgeScopes.Table)).Trim().ToLowerInvariant();
            if (scope != SqlKnowledgeScopes.Database && scope != SqlKnowledgeScopes.Table)
                return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "scope must be database or table.");
            if (scope == SqlKnowledgeScopes.Table && String.IsNullOrWhiteSpace(table))
                return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "Argument 'table' is required when scope is table.");
            string mode = (arguments.GetString("mode") ?? "append").Trim().ToLowerInvariant();
            if (mode != "append" && mode != "replace")
                return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "Unsupported mode '" + mode + "'. Use append or replace.");
            string content = (arguments.GetString("content") ?? String.Empty).Trim();
            if (content.Length == 0) return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "Argument 'content' is required.");
            if (content.Length > _MaxUpdateChars)
                return ToolResult.Error(ConnectorErrorCodes.TooLarge, "content is " + content.Length + " characters; one update may hold at most " + _MaxUpdateChars + ". Record a concise, reusable fact.");

            List<string> secrets = context.Resource.Secrets.AllValues();
            string? refusal = _Guard.Check(content, secrets);
            if (refusal != null) return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, refusal);
            string? reason = arguments.GetString("reason");

            return await WithCrawlAsync(context, arguments, async (snapshot, ct) =>
            {
                string key = String.Empty;
                if (scope == SqlKnowledgeScopes.Table)
                {
                    List<TableDetail> matches = TableIdentity.Find(snapshot.Detail, table);
                    if (matches.Count == 0)
                        return ToolResult.Error(ConnectorErrorCodes.NotFound, "Table '" + table + "' was not found in schema '" + snapshot.Detail.Schema + "'. Use sql_list_tables to see what exists.");
                    if (matches.Count > 1)
                        return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "Table '" + table + "' matches several tables; qualify it as schema.table.");
                    key = TableIdentity.Key(matches[0]);
                }

                IResourceKnowledgeStore store = context.Resource.Knowledge;
                ResourceKnowledgeEntry? existing = await store.GetAsync(scope, key, ct).ConfigureAwait(false);
                bool hasExisting = existing != null && !String.IsNullOrWhiteSpace(existing.Content);
                if (hasExisting && existing!.Source == KnowledgeSourceEnum.Operator && mode == "replace")
                    return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "The " + Describe(scope, key) + " context was written by an operator, so an agent may not replace it. Use mode=append to add your finding, or ask the operator to edit it.");

                string merged = mode == "append" && hasExisting ? existing!.Content.TrimEnd() + "\n\n" + content : content;
                string? mergedRefusal = _Guard.Check(merged, secrets);
                if (mergedRefusal != null)
                    return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, mergedRefusal + (mode == "append" ? " Use mode=replace with a curated version if the entry has grown too long." : String.Empty));

                ResourceKnowledgeEntry stored = await store.PutAsync(new ResourceKnowledgeEntry
                {
                    Scope = scope,
                    Key = key,
                    Content = merged,
                    Source = hasExisting && existing!.Source == KnowledgeSourceEnum.Operator ? KnowledgeSourceEnum.Operator : KnowledgeSourceEnum.Agent,
                    UpdatedBy = Actor(context, "agent")
                }, ct).ConfigureAwait(false);

                StringBuilder sb = new StringBuilder();
                sb.Append(mode == "append" && hasExisting ? "Appended to " : "Saved ").Append(Describe(scope, key)).Append(" context (version ")
                    .Append(stored.Version.ToString(CultureInfo.InvariantCulture)).Append(", source ").Append(stored.Source).Append(").");
                if (!String.IsNullOrWhiteSpace(reason)) sb.Append(" Reason: ").Append(reason.Trim());
                sb.Append("\n\n").Append(stored.Content).Append('\n');
                context.Resource.Logger.Info("sql_update_context " + mode + " " + Describe(scope, key) + " on " + context.Resource.Name + " version " + stored.Version);
                return BoundedPayload(context, sb.ToString(), stored, 1);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string Describe(string scope, string key)
        {
            return scope == SqlKnowledgeScopes.Database ? "database" : "table " + key;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_update_context",
                Title = "Update durable context",
                Description = "Record a durable, reusable discovery in this database's context so future conversations start smarter: a confirmed join path, a business definition (for example what counts as revenue or an active account), a naming convention, or a correction to stale context. "
                    + "scope database (key facts about the whole database) or table (facts about one table). mode append (default) adds to the entry; replace rewrites it and is refused for operator-authored entries. "
                    + "Never record secrets, credentials, personal data, raw result rows, one-off answer values, or guesses; label inferred relationships as inferred. Changes Hugo's knowledge, not the database.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"scope\":{\"type\":\"string\",\"enum\":[\"database\",\"table\"],\"description\":\"database or table. Defaults to table when table is given, else database.\"},"
                    + "\"table\":{\"type\":\"string\",\"description\":\"Table (bare or schema-qualified) when scope is table.\"},"
                    + "\"content\":{\"type\":\"string\",\"description\":\"Concise Markdown to record.\"},"
                    + "\"mode\":{\"type\":\"string\",\"enum\":[\"append\",\"replace\"],\"description\":\"append (default) or replace.\"},"
                    + "\"reason\":{\"type\":\"string\",\"description\":\"Why this is being recorded (shown in the result and logs). Optional.\"},"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema used to resolve the table. Defaults to the resource's default schema.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl before resolving the table. Default false.\"}"
                    + "},\"required\":[\"content\"],\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                UpdatesKnowledge = true,
                OutputDescription = "Confirmation with the stored content; structured content is the stored entry { scope, key, content, source, updatedBy, version, lastUpdateUtc }."
            };
        }

        #endregion
    }
}
