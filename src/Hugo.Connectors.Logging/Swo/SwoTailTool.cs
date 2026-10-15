namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>logs_tail</c> for SolarWinds Observability: the most recent page of events, then only events newer than the
    /// last one returned (by time, de-duplicated by ID). Read. Thread-safe.
    /// </summary>
    public class SwoTailTool : LoggingToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public SwoTailTool(LoggingConnectorOptions options) : base(LogsToolSchemas.Tail(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            SwoClient client = SwoClient.FromResource(context.Resource);
            SwoSettings settings = client.Settings;
            int limit = ResolveLimit(arguments, Options.DefaultTailLimit, settings.MaxEvents, context.Limits);

            LogCursor state;
            string? cursorText = arguments.GetString("cursor");
            if (!String.IsNullOrWhiteSpace(cursorText))
            {
                RejectWithCursor(arguments, "query", "providerOptions");
                state = LogCursor.Decode(cursorText, SolarWindsObservabilityConnector.TypeName, "tail");
            }
            else
            {
                LogsProviderOptions options = LogsProviderOptions.FromArguments(arguments);
                options.Require(SwoClient.ProviderName, false, true, true);
                string? query = arguments.GetString("query");
                state = new LogCursor
                {
                    Provider = SolarWindsObservabilityConnector.TypeName,
                    Kind = "tail",
                    Query = String.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                    Group = options.Group ?? settings.DefaultGroup,
                    EntityId = options.EntityId,
                    Direction = options.Direction
                };
            }

            DateTime now = DateTime.UtcNow;
            SwoLogQuery request = new SwoLogQuery
            {
                Filter = state.Query,
                Group = state.Group,
                EntityId = state.EntityId,
                Direction = state.Direction,
                PageSize = limit
            };
            if (state.LastTime.HasValue)
            {
                DateTime start = TimeRange.ToUtc(state.LastTime.Value);
                if (now - start > TimeSpan.FromHours(settings.MaxTimeRangeHours)) start = now - TimeSpan.FromHours(settings.MaxTimeRangeHours);
                request.StartTime = start;
                request.EndTime = now > start ? now : start.AddSeconds(1);
            }

            token.ThrowIfCancellationRequested();
            SwoLogsResponse response = await client.SearchLogsAsync(request, token).ConfigureAwait(false);

            List<LogEvent> events = response.Logs!
                .Select(e => e.ToLogEvent())
                .Where(e => !IsAlreadySeen(e, state))
                .OrderBy(e => e.Timestamp)
                .ToList();
            LogEventRenderer.ShortenMessages(events, Options.MaxMessageChars);
            List<LogEvent> shown = LogEventRenderer.Fit(events, context.Limits, true);

            LogEvent? newest = events.LastOrDefault();
            LogCursor next = new LogCursor
            {
                Provider = state.Provider,
                Kind = state.Kind,
                Query = state.Query,
                Group = state.Group,
                EntityId = state.EntityId,
                Direction = state.Direction,
                LastTime = newest != null ? newest.Timestamp : (state.LastTime ?? now),
                LastId = newest != null ? newest.Id : state.LastId
            };

            LogSearchPayload payload = new LogSearchPayload
            {
                Provider = SolarWindsObservabilityConnector.TypeName,
                Kind = "tail",
                Query = state.Query,
                Omitted = events.Count - shown.Count,
                NextCursor = next.Encode(),
                Events = shown
            };
            ToolResult result = LogEventRenderer.Render(payload, LogsToolSchemas.TailToolName, "To fetch only events that arrive after these", context.Limits);
            result.Metrics["fetched"] = events.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static bool IsAlreadySeen(LogEvent e, LogCursor state)
        {
            if (!state.LastTime.HasValue) return false;
            DateTime last = TimeRange.ToUtc(state.LastTime.Value);
            if (e.Timestamp < last) return true;
            return e.Timestamp == last && String.Equals(e.Id, state.LastId, StringComparison.Ordinal);
        }

        #endregion
    }
}
