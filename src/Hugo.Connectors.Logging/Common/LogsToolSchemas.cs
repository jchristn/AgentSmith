namespace Hugo.Connectors.Logging
{
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Descriptors of the SolarWinds Observability <c>logs</c> family tools. Every connector in the family builds its
    /// <c>logs_search</c> and <c>logs_tail</c> descriptors here, so a tool name always has one input schema (the
    /// family rule) and the server can route by the <c>resource</c> argument. Thread-safe.
    /// </summary>
    public static class LogsToolSchemas
    {
        #region Public-Members

        /// <summary>
        /// Name of the search tool.
        /// </summary>
        public static readonly string SearchToolName = "logs_search";

        /// <summary>
        /// Name of the tail tool.
        /// </summary>
        public static readonly string TailToolName = "logs_tail";

        /// <summary>
        /// JSON schema of the <c>providerOptions</c> object.
        /// </summary>
        public static readonly string ProviderOptionsSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,"
            + "\"description\":\"SolarWinds Observability filters: group, entityId, and direction. Unknown keys return invalid_argument.\","
            + "\"properties\":{"
            + "\"group\":{\"type\":\"string\",\"description\":\"SolarWinds Observability log group name. Defaults to the resource's defaultGroup.\"},"
            + "\"entityId\":{\"type\":\"string\",\"description\":\"SolarWinds Observability only: ID of the entity whose logs to search.\"},"
            + "\"direction\":{\"type\":\"string\",\"enum\":[\"backward\",\"forward\",\"tail\"],\"description\":\"SolarWinds Observability only: search direction. Default backward.\"}"
            + "}}";

        /// <summary>
        /// Input schema of <c>logs_search</c>.
        /// </summary>
        public static readonly string SearchInputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"query\":{\"type\":\"string\",\"description\":\"Search expression in the provider's syntax. Quote exact phrases. Omit to match every event in the window.\"},"
            + "\"startTime\":{\"type\":\"string\",\"description\":\"Window start: ISO-8601 timestamp (UTC when no offset) or a duration before now such as 15m, 2h, 1d. Default 1h.\"},"
            + "\"endTime\":{\"type\":\"string\",\"description\":\"Window end: ISO-8601 timestamp, 'now', or a duration before now. Default now.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Maximum events to return. Capped by the resource's maxEvents. Default 100.\"},"
            + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from a previous logs_search call on the same resource. Fetches the next page with the original window, query, and filters: the next page in the search direction on SolarWinds Observability. Only limit may be sent with it; query, startTime, endTime, or providerOptions alongside a cursor return invalid_argument.\"},"
            + "\"providerOptions\":" + ProviderOptionsSchema
            + "}}";

        /// <summary>
        /// Input schema of <c>logs_tail</c>.
        /// </summary>
        public static readonly string TailInputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"query\":{\"type\":\"string\",\"description\":\"Search expression in the provider's syntax. Omit for every event.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Maximum events to return. Capped by the resource's maxEvents. Default 50.\"},"
            + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from a previous logs_tail call. Returns only events newer than that call, with the original query and filters. Only limit may be sent with it; query or providerOptions alongside a cursor return invalid_argument.\"},"
            + "\"providerOptions\":" + ProviderOptionsSchema
            + "}}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the <c>logs_search</c> descriptor.
        /// </summary>
        /// <returns>Descriptor.</returns>
        public static ToolDescriptor Search()
        {
            return new ToolDescriptor
            {
                Name = SearchToolName,
                Title = "Search logs",
                Description =
                    "Search log events in a bounded time window and return them as compact lines (timestamp host program: message), plus a nextCursor when more events match. "
                    + "SolarWinds Observability lists events in the order the API uses for providerOptions.direction (backward by default, starting from the newest), and the cursor fetches the next page in that direction. "
                    + "Always bound the window: start with 15m to 1h around the incident and widen only if needed. "
                    + "Windows longer than the resource's maxTimeRangeHours are rejected with time_range_exceeded. Summarize patterns instead of pasting many lines.",
                InputSchemaJson = SearchInputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text lines plus structured JSON {provider, kind, query, startTime, endTime, count, omitted, nextCursor, events[{id, timestamp, host, program, severity, message}]}."
            };
        }

        /// <summary>
        /// Build the <c>logs_tail</c> descriptor.
        /// </summary>
        /// <returns>Descriptor.</returns>
        public static ToolDescriptor Tail()
        {
            return new ToolDescriptor
            {
                Name = TailToolName,
                Title = "Tail logs",
                Description =
                    "Return the newest log events for an optional query, newest last, plus a nextCursor. Call again with the cursor to get only events that arrived since. "
                    + "Use it to watch live behavior; use logs_search for history.",
                InputSchemaJson = TailInputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text lines plus structured JSON {provider, kind, query, count, omitted, nextCursor, events[...]}."
            };
        }

        #endregion
    }
}
