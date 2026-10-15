namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>logs_search</c> for Papertrail: a time-bounded backward search with <c>max_id</c> paging. Read. Thread-safe.
    /// </summary>
    public class PapertrailSearchTool : LoggingToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public PapertrailSearchTool(LoggingConnectorOptions options) : base(LogsToolSchemas.Search(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            PapertrailClient client = PapertrailClient.FromResource(context.Resource);
            PapertrailSettings settings = client.Settings;
            int limit = ResolveLimit(arguments, Options.DefaultSearchLimit, settings.MaxEvents, context.Limits);

            LogCursor state;
            string? cursorText = arguments.GetString("cursor");
            if (!String.IsNullOrWhiteSpace(cursorText))
            {
                RejectWithCursor(arguments, "query", "startTime", "endTime", "providerOptions");
                state = LogCursor.Decode(cursorText, PapertrailConnector.TypeName, "search");
                if (!state.Start.HasValue || !state.End.HasValue || String.IsNullOrEmpty(state.MaxId))
                    throw new ConnectorException("invalid_argument", "Argument 'cursor' is incomplete. Repeat the search without a cursor.");
            }
            else
            {
                LogsProviderOptions options = LogsProviderOptions.FromArguments(arguments);
                options.Require(PapertrailClient.ProviderName, true, false, false);
                TimeRange range = TimeRangeResolver.Resolve(arguments.GetString("startTime"), arguments.GetString("endTime"), Options.DefaultWindow, settings.MaxTimeRangeHours, DateTime.UtcNow);
                state = new LogCursor
                {
                    Provider = PapertrailConnector.TypeName,
                    Kind = "search",
                    Start = range.Start,
                    End = range.End,
                    Query = EmptyToNull(arguments.GetString("query")),
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
                MinTime = state.Start,
                MaxTime = state.End,
                MaxId = state.MaxId,
                Limit = limit
            };
            PapertrailSearchResponse response = await client.SearchAsync(request, token).ConfigureAwait(false);

            List<LogEvent> events = response.Events!.Select(e => e.ToLogEvent()).ToList();
            LogEventRenderer.ShortenMessages(events, Options.MaxMessageChars);
            List<LogEvent> shown = LogEventRenderer.Fit(events, context.Limits, true);
            int omitted = events.Count - shown.Count;

            string? next = null;
            if (omitted > 0 && shown.Any())
            {
                next = NextCursor(state, shown.First().Id);
            }
            else if (response.HasOlder)
            {
                string? oldest = events.Any() ? events.First().Id : null;
                next = NextCursor(state, MinId(response.MinId, oldest));
            }

            LogSearchPayload payload = new LogSearchPayload
            {
                Provider = PapertrailConnector.TypeName,
                Kind = "search",
                Query = state.Query,
                StartTime = state.Start,
                EndTime = state.End,
                Omitted = omitted,
                NextCursor = next,
                Events = shown
            };
            ToolResult result = LogEventRenderer.Render(payload, LogsToolSchemas.SearchToolName, "More older events are available", context.Limits);
            result.Metrics["fetched"] = events.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static string NextCursor(LogCursor state, string? maxId)
        {
            LogCursor next = new LogCursor
            {
                Provider = state.Provider,
                Kind = state.Kind,
                Start = state.Start,
                End = state.End,
                Query = state.Query,
                System = state.System,
                Group = state.Group,
                MaxId = maxId
            };
            return next.Encode();
        }

        private static string? MinId(string? a, string? b)
        {
            if (String.IsNullOrEmpty(a)) return b;
            if (String.IsNullOrEmpty(b)) return a;
            if (Decimal.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out decimal da)
                && Decimal.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out decimal db)) return da <= db ? a : b;
            return a;
        }

        private static string? EmptyToNull(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
