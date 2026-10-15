namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Intelligence;

    /// <summary>
    /// <c>sql_get_agent_pack</c>: one bounded briefing with intelligence, relationships, database context, and table
    /// context summaries (Tablix <c>tablix_get_agent_pack</c>). The first call an agent should make on a database.
    /// Thread-safe.
    /// </summary>
    public class SqlGetAgentPackTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlGetAgentPackTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            AgentPackOptions options = new AgentPackOptions
            {
                MaxEntities = arguments.GetInt("maxEntities", 25, 1, 1000),
                MaxRelationships = arguments.GetInt("maxRelationships", 40, 1, 1000),
                MaxTableContexts = arguments.GetInt("maxTableContexts", 25, 0, 1000),
                TableContextChars = arguments.GetInt("tableContextChars", 400, 80, 20000),
                LimitSyntax = Dialect.LimitSyntax,
                EngineDisplayName = Dialect.DisplayName
            };

            return await WithCrawlAsync(context, arguments, async (snapshot, ct) =>
            {
                SchemaKnowledge knowledge = await LoadKnowledgeAsync(context, ct).ConfigureAwait(false);
                DatabaseIntelligence intelligence = DatabaseIntelligenceBuilder.Build(snapshot.Detail, knowledge);
                if (!String.IsNullOrEmpty(intelligence.DatabaseName)) intelligence.DatabaseName = System.IO.Path.GetFileName(intelligence.DatabaseName);
                AgentPack pack = AgentPackBuilder.Build(intelligence, knowledge, options);
                ToolResult result = BoundedPayload(context, pack.Markdown, pack, pack.Domain.Entities.Count);
                result.MimeType = "text/markdown";
                result.Metrics["cached"] = snapshot.Cached ? 1 : 0;
                result.Metrics["contextQuality"] = pack.ContextQuality.Score;
                return result;
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_get_agent_pack",
                Title = "Get agent pack",
                Description = "Start here on any database. Returns one bounded Markdown briefing: how to work safely with this resource, the saved database context, main entities, declared and inferred relationships, likely metrics, filters, freshness columns, "
                    + "ambiguous terms to clarify, saved table context summaries, context gaps, and starter questions. Caps keep it small; page further with sql_list_tables, sql_list_relationships, and sql_get_context.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"schema\":{\"type\":\"string\",\"description\":\"Schema (MySQL: database). Defaults to the resource's default schema.\"},"
                    + "\"maxEntities\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Entities listed. Default 25.\"},"
                    + "\"maxRelationships\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Relationships listed, declared first. Default 40.\"},"
                    + "\"maxTableContexts\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":1000,\"description\":\"Table contexts summarized. Default 25.\"},"
                    + "\"tableContextChars\":{\"type\":\"integer\",\"minimum\":80,\"maximum\":20000,\"description\":\"Characters kept per table context. Default 400.\"},"
                    + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Recrawl instead of using the cached crawl. Default false.\"}"
                    + "},\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Markdown briefing; structured content { engine, databaseName, schema, contextQuality, databaseContext, databaseContextSource, domain, relationships, ambiguities, tableContexts, instructions, suggestedQuestions, omittedEntities, omittedRelationships, omittedTableContexts, markdown }."
            };
        }

        #endregion
    }
}
