namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>logs_tail</c> for Papertrail: the newest events, then only newer ones by passing the previous
    /// <c>max_id</c> as <c>min_id</c>. Read. Thread-safe.
    /// </summary>
    public class PapertrailTailTool : LoggingToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public PapertrailTailTool(LoggingConnectorOptions options) : base(LogsToolSchemas.Tail(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            PapertrailClient client = PapertrailClient.FromResource(context.Resource);
            PapertrailSettings settings = client.Settings;
            int limit = ResolveLimit(arguments, Options.DefaultTailLimit, settings.MaxEvents, context.Limits);

            LogCursor state;
            string? cursorText = arguments.GetString("cursor");
            if (!String.IsNullOrWhiteSpace(cursorText))
            {
                RejectWithCursor(arguments, "query", "providerOptions");
                state = LogCursor.Decode(cursorText, PapertrailConnector.TypeName, "tail");
            }
            else
            {
                LogsProviderOptions options = LogsProviderOptions.FromArguments(arguments);
                options.Require(PapertrailClient.ProviderName, true, false, false);
                string? query = arguments.GetString("query");
                state = new LogCursor
                {
                    Provider = PapertrailConnector.TypeName,
                    Kind = "tail",
                    Query = String.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                    System = options.System ?? settings.DefaultSystem,
                    Group = await client.ResolveGroupIdAsync(options.Group ?? settings.DefaultGroup, token).ConfigureAwait(false)
                };
            }

            token.ThrowIfCancellationRequested();
            PapertrailSearchRequest request = new PapertrailSearchRequest
            {
                Query = state.Query,
                SystemId = state.System,
                GroupId = state.Group,
                MinId = state.MinId,
                Limit = limit
            };
            PapertrailSearchResponse response = await client.SearchAsync(request, token).ConfigureAwait(false);

            List<LogEvent> events = response.Events!.Select(e => e.ToLogEvent()).ToList();
            LogEventRenderer.ShortenMessages(events, Options.MaxMessageChars);
            List<LogEvent> shown = LogEventRenderer.Fit(events, context.Limits, true);

            string? newest = events.Any() ? events.Last().Id : null;
            LogCursor next = new LogCursor
            {
                Provider = state.Provider,
                Kind = state.Kind,
                Query = state.Query,
                System = state.System,
                Group = state.Group,
                MinId = newest ?? (String.IsNullOrEmpty(response.MaxId) ? state.MinId : response.MaxId)
            };

            LogSearchPayload payload = new LogSearchPayload
            {
                Provider = PapertrailConnector.TypeName,
                Kind = "tail",
                Query = state.Query,
                Omitted = events.Count - shown.Count,
                NextCursor = String.IsNullOrEmpty(next.MinId) ? null : next.Encode(),
                Events = shown
            };
            ToolResult result = LogEventRenderer.Render(payload, LogsToolSchemas.TailToolName, "To fetch only events that arrive after these", context.Limits);
            result.Metrics["fetched"] = events.Count;
            return result;
        }

        #endregion
    }
}
