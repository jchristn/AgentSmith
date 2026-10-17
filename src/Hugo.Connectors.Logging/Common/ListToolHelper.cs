namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Shared rendering for list tools: a bounded pipe table plus structured JSON. Thread-safe.
    /// </summary>
    public static class ListToolHelper
    {
        #region Public-Members

        /// <summary>
        /// Input schema with an optional case-insensitive name filter.
        /// </summary>
        public static readonly string NameFilterSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"nameContains\":{\"type\":\"string\",\"description\":\"Only items whose name contains this text (case-insensitive).\"}"
            + "}}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a name passes the optional filter.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="filter">Filter, or null for all.</param>
        /// <returns>True when included.</returns>
        public static bool Matches(string? name, string? filter)
        {
            if (String.IsNullOrWhiteSpace(filter)) return true;
            return (name ?? String.Empty).Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Render a table of rows, capped by the call's row, line, and byte limits.
        /// </summary>
        /// <param name="title">Headline (for example <c>SolarWinds Observability entities</c>).</param>
        /// <param name="columns">Columns.</param>
        /// <param name="rows">Rows.</param>
        /// <param name="structured">Structured payload to serialize.</param>
        /// <param name="limits">Output limits.</param>
        /// <param name="footer">Optional footer line, or null.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static ToolResult Render(string title, IList<string> columns, List<IList<string?>> rows, object structured, OutputLimits limits, string? footer = null)
        {
            ArgumentNullException.ThrowIfNull(columns);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(structured);
            ArgumentNullException.ThrowIfNull(limits);

            int total = rows.Count;
            List<IList<string?>> shown = rows.Take(limits.MaxRows).ToList();
            string text = title + ": " + total.ToString(CultureInfo.InvariantCulture) + (total == 1 ? " item" : " items") + "\n";
            if (shown.Any()) text += TextTable.Render(columns, shown);
            if (shown.Count < total) text += "Showing the first " + shown.Count.ToString(CultureInfo.InvariantCulture) + "; use nameContains to narrow the list.\n";
            if (!String.IsNullOrEmpty(footer)) text += footer + "\n";

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(text, limits.MaxBytes, limits.MaxLines, result);
            result.Truncated = result.Truncated || shown.Count < total;
            result.StructuredJson = LoggingJson.Serialize(structured);
            result.Metrics["rows"] = shown.Count;
            return result;
        }

        /// <summary>
        /// Format an optional time as <c>yyyy-MM-ddTHH:mm:ssZ</c>.
        /// </summary>
        /// <param name="value">Time, or null.</param>
        /// <returns>Text, or null.</returns>
        public static string? FormatTime(DateTimeOffset? value)
        {
            return value.HasValue ? TimeRange.Format(value.Value.UtcDateTime) : null;
        }

        #endregion
    }
}
