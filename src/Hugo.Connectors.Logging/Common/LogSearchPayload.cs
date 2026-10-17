namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>logs_search</c> and <c>logs_tail</c> (MCP <c>structuredContent</c>).
    /// </summary>
    public class LogSearchPayload
    {
        /// <summary>
        /// Connector type (for example <c>solarwinds-observability</c>).
        /// </summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = String.Empty;

        /// <summary>
        /// Tool kind: <c>search</c> or <c>tail</c>.
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Query that was sent, or null for all events.
        /// </summary>
        [JsonPropertyName("query")]
        public string? Query { get; set; } = null;

        /// <summary>
        /// Window start, UTC. Null for tail.
        /// </summary>
        [JsonPropertyName("startTime")]
        public DateTime? StartTime { get; set; } = null;

        /// <summary>
        /// Window end, UTC. Null for tail.
        /// </summary>
        [JsonPropertyName("endTime")]
        public DateTime? EndTime { get; set; } = null;

        /// <summary>
        /// Number of events in <see cref="Events"/>.
        /// </summary>
        [JsonPropertyName("count")]
        public int Count { get; set; } = 0;

        /// <summary>
        /// Events fetched but left out to stay within output limits.
        /// </summary>
        [JsonPropertyName("omitted")]
        public int Omitted { get; set; } = 0;

        /// <summary>
        /// Cursor for the next page (older events for search, newer events for tail), or null when there is none.
        /// </summary>
        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; set; } = null;

        /// <summary>
        /// Events in SolarWinds Observability search order, or oldest first for tail.
        /// </summary>
        [JsonPropertyName("events")]
        public List<LogEvent> Events { get; set; } = new List<LogEvent>();
    }
}
