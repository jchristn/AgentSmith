namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Numerics;
    using System.Text;

    /// <summary>
    /// Renders Loki <c>streams</c> results as compact lines: a legend maps short stream keys (s1, s2, ...) to label
    /// sets, and each line reads <c>timestamp s1 line</c>. Thread-safe.
    /// </summary>
    public static class LokiStreamRenderer
    {
        #region Public-Methods

        /// <summary>
        /// Render streams.
        /// </summary>
        /// <param name="streams">Streams.</param>
        /// <param name="query">Query, echoed in the structured output.</param>
        /// <param name="backward">True for newest first, false for oldest first.</param>
        /// <param name="maxLines">Most lines to return.</param>
        /// <param name="maxLineChars">Longest line kept; longer lines end with <c>...</c>.</param>
        /// <param name="window">Window, or null for an instant query.</param>
        /// <returns>Rendering.</returns>
        /// <exception cref="ArgumentNullException">Thrown when streams is null.</exception>
        public static ObservabilityRendering Render(List<LokiStream> streams, string query, bool backward, int maxLines, int maxLineChars, ObservabilityTimeWindow? window)
        {
            ArgumentNullException.ThrowIfNull(streams);
            List<string> legend = new List<string>();
            List<LokiLine> lines = new List<LokiLine>();
            for (int i = 0; i < streams.Count; i++)
            {
                string key = "s" + (i + 1).ToString(CultureInfo.InvariantCulture);
                legend.Add(key + " = " + PromResultRenderer.LabelSet(streams[i].Stream));
                foreach (LokiEntry e in streams[i].Values)
                {
                    lines.Add(new LokiLine
                    {
                        StreamKey = key,
                        StreamIndex = i,
                        Nanoseconds = ParseNs(e.Timestamp),
                        Line = Shorten(e.Line, maxLineChars)
                    });
                }
            }
            List<LokiLine> ordered = backward
                ? lines.OrderByDescending(l => l.Nanoseconds).ToList()
                : lines.OrderBy(l => l.Nanoseconds).ToList();
            int cap = Math.Max(1, maxLines);
            List<LokiLine> shown = ordered.Take(cap).ToList();
            int omitted = ordered.Count - shown.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(ordered.Count.ToString(CultureInfo.InvariantCulture)).Append(" line(s) from ").Append(streams.Count.ToString(CultureInfo.InvariantCulture)).Append(" stream(s), ").Append(backward ? "newest first" : "oldest first");
            if (window != null) sb.Append(", window ").Append(ObservabilityTime.Format(window.Start)).Append(" to ").Append(ObservabilityTime.Format(window.End));
            if (omitted > 0) sb.Append(", showing ").Append(shown.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
            if (legend.Count > 0)
            {
                sb.Append("Streams:\n");
                foreach (string l in legend) sb.Append("  ").Append(l).Append('\n');
            }
            foreach (LokiLine l in shown)
            {
                sb.Append(ObservabilityTime.FormatPrecise(ToTime(l.Nanoseconds))).Append(' ').Append(l.StreamKey).Append(' ').Append(l.Line.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
            }
            if (omitted > 0) sb.Append("[").Append(omitted.ToString(CultureInfo.InvariantCulture)).Append(" more line(s) not shown; narrow the query or the window]\n");

            string structured = ObservabilityJson.Serialize(new Dictionary<string, object?>
            {
                { "resultType", "streams" },
                { "query", query },
                { "start", window == null ? null : ObservabilityTime.Format(window.Start) },
                { "end", window == null ? null : ObservabilityTime.Format(window.End) },
                { "count", shown.Count },
                { "omitted", omitted },
                { "streams", streams.Select(s => s.Stream).ToList() },
                { "lines", shown.Select(l => new Dictionary<string, object> { { "time", ObservabilityTime.FormatPrecise(ToTime(l.Nanoseconds)) }, { "stream", l.StreamIndex }, { "line", l.Line } }).ToList() }
            });

            return new ObservabilityRendering
            {
                Text = sb.ToString(),
                StructuredJson = structured,
                Truncated = omitted > 0,
                Returned = shown.Count,
                Omitted = omitted
            };
        }

        #endregion

        #region Private-Methods

        private static BigInteger ParseNs(string text)
        {
            return BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger v) ? v : BigInteger.Zero;
        }

        private static DateTimeOffset ToTime(BigInteger ns)
        {
            BigInteger ticks = ns / 100;
            if (ticks < 0 || ticks > (DateTimeOffset.MaxValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks)) return DateTimeOffset.UnixEpoch;
            return DateTimeOffset.UnixEpoch.AddTicks((long)ticks);
        }

        private static string Shorten(string value, int max)
        {
            string v = value ?? String.Empty;
            if (max < 16) max = 16;
            return v.Length > max ? v.Substring(0, max - 3) + "..." : v;
        }

        #endregion
    }
}
