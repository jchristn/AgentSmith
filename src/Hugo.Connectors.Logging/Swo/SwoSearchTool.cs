namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>logs_search</c> for SolarWinds Observability: a time-bounded search with <c>skipToken</c> paging. Read.
    /// Thread-safe.
    /// </summary>
    public class SwoSearchTool : LoggingToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public SwoSearchTool(LoggingConnectorOptions options) : base(LogsToolSchemas.Search(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            SwoClient client = SwoClient.FromResource(context.Resource);
            SwoSettings settings = client.Settings;
            int limit = ResolveLimit(arguments, Options.DefaultSearchLimit, settings.MaxEvents, context.Limits);

            LogCursor state;
            string? cursorText = arguments.GetString("cursor");
            if (!String.IsNullOrWhiteSpace(cursorText))
            {
                state = LogCursor.Decode(cursorText, SolarWindsObservabilityConnector.TypeName, "search");
                if (!state.Start.HasValue || !state.End.HasValue || String.IsNullOrEmpty(state.SkipToken))
                    throw new ConnectorException("invalid_argument", "Argument 'cursor' is incomplete. Repeat the search without a cursor.");
            }
            else
            {
                LogsProviderOptions options = LogsProviderOptions.FromArguments(arguments);
                options.Require(SwoClient.ProviderName, false, true, true);
                TimeRange range = TimeRangeResolver.Resolve(arguments.GetString("startTime"), arguments.GetString("endTime"), Options.DefaultWindow, settings.MaxTimeRangeHours, DateTime.UtcNow);
                string? query = arguments.GetString("query");
                state = new LogCursor
                {
                    Provider = SolarWindsObservabilityConnector.TypeName,
                    Kind = "search",
                    Start = range.Start,
                    End = range.End,
                    Query = String.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                    Group = options.Group ?? settings.DefaultGroup,
                    EntityId = options.EntityId,
                    Direction = options.Direction
                };
            }

            token.ThrowIfCancellationRequested();
            SwoLogQuery request = new SwoLogQuery
            {
                Filter = state.Query,
                Group = state.Group,
                EntityId = state.EntityId,
                StartTime = state.Start,
                EndTime = state.End,
                Direction = state.Direction,
                PageSize = limit,
                SkipToken = state.SkipToken
            };
            SwoLogsResponse response = await client.SearchLogsAsync(request, token).ConfigureAwait(false);

            List<LogEvent> events = response.Logs!.Select(e => e.ToLogEvent()).ToList();
            LogEventRenderer.ShortenMessages(events, Options.MaxMessageChars);
            List<LogEvent> shown = LogEventRenderer.Fit(events, context.Limits, false);

            string? skip = SwoClient.ExtractSkipToken(response.PageInfo == null ? null : response.PageInfo.NextPage);
            string? next = null;
            if (!String.IsNullOrEmpty(skip) && events.Any())
            {
                LogCursor nextCursor = new LogCursor
                {
                    Provider = state.Provider,
                    Kind = state.Kind,
                    Start = state.Start,
                    End = state.End,
                    Query = state.Query,
                    Group = state.Group,
                    EntityId = state.EntityId,
                    Direction = state.Direction,
                    SkipToken = skip
                };
                next = nextCursor.Encode();
            }

            LogSearchPayload payload = new LogSearchPayload
            {
                Provider = SolarWindsObservabilityConnector.TypeName,
                Kind = "search",
                Query = state.Query,
                StartTime = state.Start,
                EndTime = state.End,
                Omitted = events.Count - shown.Count,
                NextCursor = next,
                Events = shown
            };
            ToolResult result = LogEventRenderer.Render(payload, LogsToolSchemas.SearchToolName, "More events are available", context.Limits);
            result.Metrics["fetched"] = events.Count;
            return result;
        }

        #endregion
    }
}
