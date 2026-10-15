namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds bounded tool results from text and tables. Thread-safe.
    /// </summary>
    public static class RedisOutput
    {
        #region Public-Methods

        /// <summary>
        /// Build a result from a header line and a table payload. Rows beyond the row limit are dropped and reported.
        /// </summary>
        /// <param name="header">Header text (one or more lines), or empty.</param>
        /// <param name="payload">Table payload. Rows beyond <paramref name="limits"/> are removed from it.</param>
        /// <param name="limits">Output limits.</param>
        /// <returns>Result with text and structured JSON.</returns>
        /// <exception cref="ArgumentNullException">Thrown when payload or limits is null.</exception>
        public static ToolResult Table(string header, RedisRowsPayload payload, OutputLimits limits)
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(limits);
            if (payload.Total < payload.Rows.Count) payload.Total = payload.Rows.Count;
            if (payload.Rows.Count > limits.MaxRows)
            {
                payload.Rows = payload.Rows.Take(limits.MaxRows).ToList();
                payload.Truncated = true;
            }

            StringBuilder sb = new StringBuilder();
            if (!String.IsNullOrEmpty(header)) sb.Append(header.TrimEnd('\n')).Append('\n');
            if (payload.Rows.Any())
            {
                IList<IList<string?>> rows = payload.Rows.Select(r => (IList<string?>)r).ToList();
                sb.Append(TextTable.Render(payload.Columns, rows));
            }
            if (payload.Truncated) sb.Append("[showing ").Append(payload.Rows.Count).Append(" of ").Append(payload.Total).Append(" rows]\n");
            if (payload.Notes != null)
            {
                foreach (KeyValuePair<string, string> note in payload.Notes) sb.Append(note.Key).Append(": ").Append(note.Value).Append('\n');
            }

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), limits.MaxBytes, limits.MaxLines, result);
            result.Truncated = result.Truncated || payload.Truncated;
            result.StructuredJson = RedisJson.Serialize(payload);
            result.Metrics["rows"] = payload.Rows.Count;
            return result;
        }

        /// <summary>
        /// Build a result from text and an optional structured object, bounded by the limits.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="structured">Structured object, or null.</param>
        /// <param name="limits">Output limits.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when limits is null.</exception>
        public static ToolResult Text(string text, object? structured, OutputLimits limits)
        {
            ArgumentNullException.ThrowIfNull(limits);
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(text, limits.MaxBytes, limits.MaxLines, result);
            if (structured != null) result.StructuredJson = RedisJson.Serialize(structured);
            return result;
        }

        /// <summary>
        /// Human form of a TTL in milliseconds: <c>no expiry</c>, <c>key does not exist</c>, or seconds.
        /// </summary>
        /// <param name="ttlMs">PTTL reply.</param>
        /// <returns>Text.</returns>
        public static string Ttl(long ttlMs)
        {
            if (ttlMs == -1) return "no expiry";
            if (ttlMs == -2) return "key does not exist";
            if (ttlMs < 0) return ttlMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return (ttlMs / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }

        #endregion
    }
}
