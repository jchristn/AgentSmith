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
    /// <c>sql_get_context</c>: read durable database and table context (Tablix <c>tablix_get_database_context</c> and
    /// <c>tablix_get_table_context</c>). Thread-safe.
    /// </summary>
    public class SqlGetContextTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlGetContextTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            List<string> requested = arguments.GetStringList("tables");
            string? single = arguments.GetString("table");
            if (!String.IsNullOrWhiteSpace(single)) requested.Insert(0, single);
            bool listTables = arguments.GetBool("listTables", false);
            bool includeEmpty = arguments.GetBool("includeEmpty", false);
            string? cursor = arguments.GetString("cursor");
            int pageSize = Math.Min(arguments.GetInt("pageSize", 50, 1, 1000), Math.Max(1, context.Limits.MaxRows));

            return await WithCrawlAsync(context, arguments, async (snapshot, ct) =>
            {
                SchemaKnowledge knowledge = await LoadKnowledgeAsync(context, ct).ConfigureAwait(false);
                SchemaDetail detail = snapshot.Detail;
                ContextReadResult read = new ContextReadResult { Schema = detail.Schema, Database = knowledge.Database };
                StringBuilder sb = new StringBuilder();
                sb.Append("## Database context");
                if (knowledge.HasDatabaseContext) sb.Append(Provenance(knowledge.Database!)).Append('\n').Append(knowledge.Database!.Content.Trim()).Append('\n');
                else sb.Append("\nNone saved. Generate it with sql_generate_context or write it with sql_update_context.\n");

                List<string> missing = new List<string>();
                foreach (string name in requested.Where(n => !String.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    List<TableDetail> matches = TableIdentity.Find(detail, name);
                    if (matches.Count == 0)
                    {
                        missing.Add(name);
                        continue;
                    }
                    foreach (TableDetail t in matches)
                    {
                        ResourceKnowledgeEntry? entry = knowledge.Table(t.SchemaName, t.TableName);
                        string key = TableIdentity.Key(t);
                        sb.Append("\n## Table ").Append(key);
                        if (entry == null)
                        {
                            read.TablesWithoutContext.Add(key);
                            sb.Append("\nNo context saved for this table yet.\n");
                        }
                        else
                        {
                            read.Tables.Add(entry);
                            sb.Append(Provenance(entry)).Append('\n').Append(entry.Content.Trim()).Append('\n');
                        }
                    }
                }
                if (missing.Count > 0)
                    return ToolResult.Error(ConnectorErrorCodes.NotFound, "Table(s) not found in schema '" + detail.Schema + "': " + String.Join(", ", missing) + ". Use sql_list_tables to see what exists.");

                if (listTables)
                {
                    List<string> keys = includeEmpty
                        ? detail.Tables.Select(TableIdentity.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
                        : detail.Tables.Where(t => knowledge.Table(t.SchemaName, t.TableName) != null).Select(TableIdentity.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                    int offset = SchemaProjection.ParseCursor(cursor);
                    List<string> page = keys.Skip(offset).Take(pageSize).ToList();
                    read.TotalRecords = keys.Count;
                    read.NextCursor = offset + page.Count < keys.Count ? (offset + page.Count).ToString(CultureInfo.InvariantCulture) : null;
                    sb.Append("\n## Table context (").Append(keys.Count).Append(includeEmpty ? " tables" : " tables with context").Append(")\n");
                    foreach (string key in page)
                    {
                        ResourceKnowledgeEntry? entry = knowledge.Tables.TryGetValue(key, out ResourceKnowledgeEntry? e) ? e : null;
                        if (entry == null)
                        {
                            read.TablesWithoutContext.Add(key);
                            sb.Append("### ").Append(key).Append("\n(no context)\n");
                            continue;
                        }
                        read.Tables.Add(entry);
                        sb.Append("### ").Append(key).Append(Provenance(entry)).Append('\n').Append(entry.Content.Trim()).Append('\n');
                    }
                    if (read.NextCursor != null) sb.Append("More tables: call again with cursor \"").Append(read.NextCursor).Append("\".\n");
                }
                else if (requested.Count == 0)
                {
                    int count = detail.Tables.Count(t => knowledge.Table(t.SchemaName, t.TableName) != null);
                    sb.Append('\n').Append(count).Append(" of ").Append(detail.Tables.Count).Append(" tables have context. Pass table, tables, or listTables=true to read it.\n");
                }

                sb.Append("\nContext is guidance, not proof: verify names against sql_describe_table before querying.\n");
                return BoundedPayload(context, sb.ToString(), read, read.Tables.Count);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string Provenance(ResourceKnowledgeEntry entry)
        {
            return " (" + entry.Source + ", version " + entry.Version.ToString(CultureInfo.InvariantCulture) + ", updated "
                + entry.LastUpdateUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" + (String.IsNullOrWhiteSpace(entry.UpdatedBy) ? String.Empty : " by " + entry.UpdatedBy) + ")";
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_get_context",
                Title = "Get durable context",
                Description = "Read the durable context saved for this database: the database-level context (purpose, entities, business definitions, join guidance, caveats) and, on request, table-level context. "
                    + "Each entry shows who wrote it (Operator, Generated, Agent) and when. Use it before answering questions whose meaning matters (revenue, active, status). "
                    + "Pass table or tables for specific tables, or listTables=true to page through every table context (includeEmpty=true also lists tables without one). Context is guidance; verify names before querying.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Defaults to the resource's default schema.\"},"
                    + "\"table\":{\"type\":\"string\",\"description\":\"One table (bare or schema-qualified). Optional.\"},"
                    + "\"tables\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Several tables. Optional.\"},"
                    + "\"listTables\":{\"type\":\"boolean\",\"description\":\"Page through table contexts. Default false.\"},"
                    + "\"includeEmpty\":{\"type\":\"boolean\",\"description\":\"With listTables, include tables that have no context. Default false.\"},"
                    + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from the previous page.\"},"
                    + "\"pageSize\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Table contexts per page. Default 50.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl instead of using the cached crawl. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Markdown sections; structured content { schema, database: { scope, key, content, source, updatedBy, version, lastUpdateUtc }, tables: [...], tablesWithoutContext, totalRecords, nextCursor }."
            };
        }

        #endregion
    }
}
