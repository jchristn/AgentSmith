namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Renders log events as compact lines (<c>timestamp host program: message</c>) bounded by the call's output
    /// limits, and builds the tool result with structured content. Thread-safe.
    /// </summary>
    public static class LogEventRenderer
    {
        #region Public-Methods

        /// <summary>
        /// Shorten messages longer than the limit and flatten line breaks.
        /// </summary>
        /// <param name="events">Events. Modified in place.</param>
        /// <param name="maxMessageChars">Maximum characters per message.</param>
        /// <exception cref="ArgumentNullException">Thrown when events is null.</exception>
        public static void ShortenMessages(List<LogEvent> events, int maxMessageChars)
        {
            ArgumentNullException.ThrowIfNull(events);
            int max = Math.Max(16, maxMessageChars);
            foreach (LogEvent e in events)
            {
                string m = (e.Message ?? String.Empty).Replace("\r\n", "\\n").Replace('\n', ' ').Replace('\r', ' ');
                if (m.Length > max) m = m.Substring(0, max - 3) + "...";
                e.Message = m;
            }
        }

        /// <summary>
        /// Format one event as a single line.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <returns>Line without a trailing newline.</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static string FormatLine(LogEvent e)
        {
            ArgumentNullException.ThrowIfNull(e);
            StringBuilder sb = new StringBuilder();
            sb.Append(e.Timestamp == DateTime.MinValue ? "-" : TimeRange.Format(e.Timestamp));
            sb.Append(' ').Append(String.IsNullOrEmpty(e.Host) ? "-" : e.Host);
            if (!String.IsNullOrEmpty(e.Program)) sb.Append(' ').Append(e.Program);
            sb.Append(": ").Append(e.Message ?? String.Empty);
            return sb.ToString();
        }

        /// <summary>
        /// Select the events that fit the output limits, keeping either the newest (end of the list) or the oldest
        /// (start of the list). Order is preserved.
        /// </summary>
        /// <param name="events">Events in display order.</param>
        /// <param name="limits">Output limits.</param>
        /// <param name="keepEnd">True to keep the end of the list, false to keep the start.</param>
        /// <returns>Events that fit.</returns>
        /// <exception cref="ArgumentNullException">Thrown when events or limits is null.</exception>
        public static List<LogEvent> Fit(List<LogEvent> events, OutputLimits limits, bool keepEnd)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(limits);
            int maxLines = Math.Max(1, limits.MaxLines - 4);
            int maxBytes = Math.Max(512, limits.MaxBytes - 1024);
            List<LogEvent> ordered = keepEnd ? Enumerable.Reverse(events).ToList() : events.ToList();
            List<LogEvent> kept = new List<LogEvent>();
            int bytes = 0;
            foreach (LogEvent e in ordered)
            {
                int lineBytes = Encoding.UTF8.GetByteCount(FormatLine(e)) + 1;
                if (kept.Count >= maxLines || bytes + lineBytes > maxBytes) break;
                kept.Add(e);
                bytes += lineBytes;
            }
            if (keepEnd) kept.Reverse();
            return kept;
        }

        /// <summary>
        /// Build the tool result: a headline, one line per event, and a footer explaining paging and omissions.
        /// </summary>
        /// <param name="payload">Structured payload; its events are the ones rendered.</param>
        /// <param name="toolName">Tool name to mention in the paging hint (for example <c>logs_search</c>).</param>
        /// <param name="nextDescription">Sentence introducing the cursor (for example <c>More older events are available</c>).</param>
        /// <param name="limits">Output limits.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when payload or limits is null.</exception>
        public static ToolResult Render(LogSearchPayload payload, string toolName, string nextDescription, OutputLimits limits)
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(limits);
            payload.Count = payload.Events.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(payload.Provider).Append(' ').Append(payload.Kind).Append(": ").Append(payload.Count.ToString(CultureInfo.InvariantCulture)).Append(payload.Count == 1 ? " event" : " events");
            if (payload.StartTime.HasValue && payload.EndTime.HasValue)
                sb.Append(" between ").Append(TimeRange.Format(payload.StartTime.Value)).Append(" and ").Append(TimeRange.Format(payload.EndTime.Value));
            if (!String.IsNullOrEmpty(payload.Query)) sb.Append(" matching ").Append(payload.Query);
            sb.Append('\n');

            foreach (LogEvent e in payload.Events) sb.Append(FormatLine(e)).Append('\n');

            if (payload.Count == 0) sb.Append("No events matched. Widen the window or relax the query.\n");
            if (payload.Omitted > 0)
                sb.Append(payload.Omitted.ToString(CultureInfo.InvariantCulture)).Append(" fetched events were left out to stay within output limits; lower limit or narrow the query.\n");
            if (!String.IsNullOrEmpty(payload.NextCursor))
                sb.Append(nextDescription).Append(": call ").Append(toolName).Append(" with cursor \"").Append(payload.NextCursor).Append("\".\n");

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), limits.MaxBytes, limits.MaxLines, result);
            result.Truncated = result.Truncated || payload.Omitted > 0;
            result.StructuredJson = LoggingJson.Serialize(payload);
            result.Metrics["events"] = payload.Count;
            result.Metrics["omitted"] = payload.Omitted;
            return result;
        }

        #endregion
    }
}
