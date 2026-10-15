namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>swo_list_entities</c>: SolarWinds Observability entities (hosts, services, databases, and more) with health
    /// and last-seen time, paged with a cursor. Read. Thread-safe.
    /// </summary>
    public class SwoListEntitiesTool : LoggingToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"type\":{\"type\":\"string\",\"description\":\"Entity type, for example Host, Service, or DatabaseInstance. Omit for every type.\"},"
            + "\"name\":{\"type\":\"string\",\"description\":\"Case-insensitive substring of the entity name.\"},"
            + "\"search\":{\"type\":\"string\",\"description\":\"Contextual search; combined with name by AND.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Entities per page. Default 50.\"},"
            + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from a previous swo_list_entities call with the same filters.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public SwoListEntitiesTool(LoggingConnectorOptions options)
            : base(new ToolDescriptor
            {
                Name = "swo_list_entities",
                Title = "List SolarWinds Observability entities",
                Description = "List monitored entities (hosts, services, databases, websites, and more) with ID, type, name, health state, maintenance flag, "
                    + "and last-seen time. Use the ID with swo_get_entity, swo_query_metric, or logs_search providerOptions.entityId.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table id, type, name, health, inMaintenance, lastSeen plus structured JSON {entities[], nextCursor}."
            }, options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            SwoClient client = SwoClient.FromResource(context.Resource);
            int limit = arguments.GetInt("limit", Math.Min(50, context.Limits.MaxRows), 1, Math.Min(1000, context.Limits.MaxRows));
            SwoEntitiesResponse response = await client.ListEntitiesAsync(
                arguments.GetString("type"),
                arguments.GetString("name"),
                arguments.GetString("search"),
                limit,
                arguments.GetString("cursor"),
                token).ConfigureAwait(false);

            List<SwoEntity> entities = response.Entities!;
            string? next = SwoClient.ExtractSkipToken(response.PageInfo == null ? null : response.PageInfo.NextPage);
            if (!entities.Any()) next = null;

            List<IList<string?>> rows = entities
                .Select(e => (IList<string?>)new List<string?>
                {
                    e.Id,
                    e.Type,
                    String.IsNullOrEmpty(e.DisplayName) ? e.Name : e.DisplayName,
                    e.HealthState == null ? null : e.HealthState.State,
                    e.InMaintenance ? "yes" : "no",
                    ListToolHelper.FormatTime(e.LastSeenTime)
                })
                .ToList();

            Dictionary<string, object?> structured = new Dictionary<string, object?>
            {
                { "entities", entities.Take(context.Limits.MaxRows).Select(e => Summarize(e)).ToList() },
                { "nextCursor", next }
            };
            string? footer = next == null ? null : "More entities are available: call swo_list_entities with the same filters and cursor \"" + next + "\".";
            return ListToolHelper.Render("SolarWinds Observability entities", new List<string> { "id", "type", "name", "health", "inMaintenance", "lastSeen" }, rows, structured, context.Limits, footer);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, object?> Summarize(SwoEntity e)
        {
            return new Dictionary<string, object?>
            {
                { "id", e.Id },
                { "type", e.Type },
                { "name", e.Name },
                { "displayName", e.DisplayName },
                { "healthState", e.HealthState == null ? null : e.HealthState.State },
                { "inMaintenance", e.InMaintenance },
                { "lastSeenTime", e.LastSeenTime },
                { "tags", e.Tags }
            };
        }

        #endregion
    }
}
