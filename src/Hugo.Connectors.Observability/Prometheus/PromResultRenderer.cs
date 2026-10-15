namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Renders Prometheus-style query results (instant vectors, range matrices, scalars, strings) compactly for a model,
    /// with structured JSON. Also used for Loki metric queries. Thread-safe.
    /// </summary>
    public static class PromResultRenderer
    {
        #region Public-Methods

        /// <summary>
        /// Render a label set as <c>name{a="b",c="d"}</c>, with <c>__name__</c> moved to the front.
        /// </summary>
        /// <param name="labels">Labels. Null renders <c>{}</c>.</param>
        /// <returns>Text.</returns>
        public static string LabelSet(IDictionary<string, string>? labels)
        {
            if (labels == null || labels.Count == 0) return "{}";
            string name = labels.TryGetValue("__name__", out string? n) ? n : String.Empty;
            List<string> parts = labels
                .Where(kv => kv.Key != "__name__")
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=\"" + kv.Value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"")
                .ToList();
            if (parts.Count == 0) return String.IsNullOrEmpty(name) ? "{}" : name;
            return name + "{" + String.Join(",", parts) + "}";
        }

        /// <summary>
        /// Render a query result.
        /// </summary>
        /// <param name="data">Query data.</param>
        /// <param name="provider">Provider name for error messages.</param>
        /// <param name="query">The query, echoed in the output.</param>
        /// <param name="maxSeries">Most series to return.</param>
        /// <param name="window">Range window for matrix results, or null.</param>
        /// <param name="stepSeconds">Range step, or 0.</param>
        /// <param name="notes">Notes placed before the results (for example a raised step). Null means none.</param>
        /// <returns>Rendering.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="ConnectorException">Thrown when the result does not match its type.</exception>
        public static ObservabilityRendering Render(PromQueryData data, string provider, string query, int maxSeries, ObservabilityTimeWindow? window, long stepSeconds, IList<string>? notes)
        {
            ArgumentNullException.ThrowIfNull(data);
            string type = data.ResultType ?? String.Empty;
            StringBuilder sb = new StringBuilder();
            if (notes != null) foreach (string note in notes) sb.Append("Note: ").Append(note).Append('\n');

            if (type == "scalar" || type == "string")
            {
                PromPoint? point = data.Result.ValueKind == JsonValueKind.Array ? ObservabilityJson.DeserializeElement<PromPoint>(data.Result, provider) : null;
                ObservabilityRendering single = new ObservabilityRendering();
                if (point == null)
                {
                    sb.Append(type).Append(" result: (none)\n");
                }
                else
                {
                    sb.Append(type).Append(" result: ").Append(point.Value).Append(" at ").Append(ObservabilityTime.Format(ObservabilityTime.FromUnixSeconds(point.Timestamp))).Append('\n');
                    single.Returned = 1;
                }
                single.Text = sb.ToString();
                single.StructuredJson = Structured(type, query, window, stepSeconds, new List<PromSeries>(), point, 0, null);
                return single;
            }

            if (type != "vector" && type != "matrix")
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, provider + " returned an unsupported result type '" + type + "'.");

            List<PromSeries> all = data.Result.ValueKind == JsonValueKind.Array
                ? ObservabilityJson.DeserializeElement<List<PromSeries>>(data.Result, provider) ?? new List<PromSeries>()
                : new List<PromSeries>();
            int cap = Math.Max(1, maxSeries);
            List<PromSeries> shown = all.Take(cap).ToList();
            int omitted = all.Count - shown.Count;

            ObservabilityRendering rendering = new ObservabilityRendering();
            rendering.Returned = shown.Count;
            rendering.Omitted = omitted;
            rendering.Truncated = omitted > 0;

            if (type == "vector")
            {
                sb.Append(all.Count.ToString(CultureInfo.InvariantCulture)).Append(" series");
                if (omitted > 0) sb.Append(", showing the first ").Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(" (maxSeries)");
                sb.Append('\n');
                if (shown.Count > 0)
                {
                    List<IList<string?>> rows = new List<IList<string?>>();
                    foreach (PromSeries s in shown)
                    {
                        string value = s.Value == null ? String.Empty : s.Value.Value;
                        string at = s.Value == null ? String.Empty : ObservabilityTime.Format(ObservabilityTime.FromUnixSeconds(s.Value.Timestamp));
                        rows.Add(new List<string?> { LabelSet(s.Metric), value, at });
                    }
                    sb.Append(TextTable.Render(new List<string> { "series", "value", "time" }, rows));
                }
                rendering.Text = sb.ToString();
                rendering.StructuredJson = Structured(type, query, window, stepSeconds, shown, null, omitted, null);
                return rendering;
            }

            sb.Append(all.Count.ToString(CultureInfo.InvariantCulture)).Append(" series");
            if (window != null) sb.Append(" from ").Append(ObservabilityTime.Format(window.Start)).Append(" to ").Append(ObservabilityTime.Format(window.End));
            if (stepSeconds > 0) sb.Append(", step ").Append(ObservabilityTime.FormatDuration(TimeSpan.FromSeconds(stepSeconds)));
            if (omitted > 0) sb.Append(", showing the first ").Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(" (maxSeries)");
            sb.Append('\n');
            if (shown.Count > 0)
            {
                List<IList<string?>> rows = new List<IList<string?>>();
                foreach (PromSeries s in shown)
                {
                    List<PromPoint> points = s.Values ?? new List<PromPoint>();
                    List<double> numbers = points.Select(p => p.Number()).Where(d => !Double.IsNaN(d)).ToList();
                    rows.Add(new List<string?>
                    {
                        LabelSet(s.Metric),
                        points.Count.ToString(CultureInfo.InvariantCulture),
                        numbers.Count == 0 ? "NaN" : Num(numbers.Min()),
                        numbers.Count == 0 ? "NaN" : Num(numbers.Max()),
                        numbers.Count == 0 ? "NaN" : Num(numbers.Average()),
                        points.Count == 0 ? String.Empty : points[points.Count - 1].Value,
                        points.Count == 0 ? String.Empty : ObservabilityTime.Format(ObservabilityTime.FromUnixSeconds(points[0].Timestamp)),
                        points.Count == 0 ? String.Empty : ObservabilityTime.Format(ObservabilityTime.FromUnixSeconds(points[points.Count - 1].Timestamp))
                    });
                }
                sb.Append(TextTable.Render(new List<string> { "series", "points", "min", "max", "avg", "last", "first time", "last time" }, rows));

                bool longWindow = window != null && window.Length > TimeSpan.FromDays(1);
                sb.Append("\nValues (time=value, UTC):\n");
                foreach (PromSeries s in shown)
                {
                    sb.Append(LabelSet(s.Metric)).Append(": ");
                    List<PromPoint> points = s.Values ?? new List<PromPoint>();
                    sb.Append(String.Join(" ", points.Select(p => ShortTime(ObservabilityTime.FromUnixSeconds(p.Timestamp), longWindow) + "=" + p.Value)));
                    sb.Append('\n');
                }
            }
            rendering.Text = sb.ToString();
            rendering.StructuredJson = Structured(type, query, window, stepSeconds, shown, null, omitted, null);
            return rendering;
        }

        #endregion

        #region Private-Methods

        private static string Num(double value)
        {
            return value.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string ShortTime(DateTimeOffset value, bool withDate)
        {
            return value.ToUniversalTime().ToString(withDate ? "MM-dd'T'HH:mm:ss" : "HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string Structured(string type, string query, ObservabilityTimeWindow? window, long stepSeconds, List<PromSeries> series, PromPoint? scalar, int omitted, string? extra)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Utf8JsonWriter w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("resultType", type);
                    w.WriteString("query", query);
                    if (window != null)
                    {
                        w.WriteString("start", ObservabilityTime.Format(window.Start));
                        w.WriteString("end", ObservabilityTime.Format(window.End));
                    }
                    if (stepSeconds > 0) w.WriteNumber("stepSeconds", stepSeconds);
                    if (scalar != null)
                    {
                        w.WriteStartObject("value");
                        w.WriteString("time", ObservabilityTime.FormatPrecise(ObservabilityTime.FromUnixSeconds(scalar.Timestamp)));
                        w.WriteString("value", scalar.Value);
                        w.WriteEndObject();
                    }
                    w.WriteNumber("omittedSeries", omitted);
                    w.WriteStartArray("series");
                    foreach (PromSeries s in series)
                    {
                        w.WriteStartObject();
                        w.WriteStartObject("labels");
                        foreach (KeyValuePair<string, string> kv in s.Metric.OrderBy(k => k.Key, StringComparer.Ordinal)) w.WriteString(kv.Key, kv.Value);
                        w.WriteEndObject();
                        if (s.Value != null)
                        {
                            w.WriteString("time", ObservabilityTime.FormatPrecise(ObservabilityTime.FromUnixSeconds(s.Value.Timestamp)));
                            w.WriteString("value", s.Value.Value);
                        }
                        if (s.Values != null)
                        {
                            w.WriteStartArray("values");
                            foreach (PromPoint p in s.Values)
                            {
                                w.WriteStartArray();
                                w.WriteNumberValue(p.Timestamp);
                                w.WriteStringValue(p.Value);
                                w.WriteEndArray();
                            }
                            w.WriteEndArray();
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    if (extra != null) w.WriteString("note", extra);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        #endregion
    }
}
