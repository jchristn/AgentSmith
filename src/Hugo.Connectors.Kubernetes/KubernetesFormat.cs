namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Formatting helpers shared by the Kubernetes tools: ages, table cells, label maps, and JSON parsing. Thread-safe.
    /// </summary>
    public static class KubernetesFormat
    {
        #region Public-Members

        /// <summary>
        /// Case-insensitive JSON options for Hugo's own typed response classes.
        /// </summary>
        public static JsonSerializerOptions CaseInsensitive { get; } = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Indented JSON options for output.
        /// </summary>
        public static JsonSerializerOptions Indented { get; } = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Format an age like kubectl (<c>45s</c>, <c>12m</c>, <c>5h</c>, <c>3d</c>).
        /// </summary>
        /// <param name="since">Start time (UTC), or null.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <returns>Age, or <c>&lt;unknown&gt;</c>.</returns>
        public static string Age(DateTime? since, DateTime now)
        {
            if (!since.HasValue) return "<unknown>";
            TimeSpan age = now - since.Value.ToUniversalTime();
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            if (age.TotalSeconds < 120) return ((int)age.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
            if (age.TotalMinutes < 120) return ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
            if (age.TotalHours < 48) return ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + "h";
            return ((int)age.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
        }

        /// <summary>
        /// Convert a table cell to text.
        /// </summary>
        /// <param name="cell">Cell.</param>
        /// <returns>Text.</returns>
        public static string Cell(JsonElement cell)
        {
            switch (cell.ValueKind)
            {
                case JsonValueKind.String:
                    return cell.GetString() ?? String.Empty;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return "<none>";
                default:
                    return cell.GetRawText();
            }
        }

        /// <summary>
        /// Format a label or annotation map as <c>k=v,k2=v2</c>.
        /// </summary>
        /// <param name="map">Map, or null.</param>
        /// <returns>Text, or <c>&lt;none&gt;</c>.</returns>
        public static string Map(IDictionary<string, string>? map)
        {
            if (map == null || map.Count == 0) return "<none>";
            return String.Join(",", map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
        }

        /// <summary>
        /// Parse a JSON body into a node.
        /// </summary>
        /// <param name="body">Body.</param>
        /// <param name="what">What the body is, for errors.</param>
        /// <returns>Node.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>upstream_error</c> when the body is not JSON.</exception>
        public static JsonNode ParseNode(string body, string what)
        {
            try
            {
                JsonNode? node = JsonNode.Parse(body);
                if (node == null) throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The API server returned an empty response for " + what + ".");
                return node;
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The API server returned a response for " + what + " that is not JSON.", e);
            }
        }

        /// <summary>
        /// Deserialize a typed object (KubernetesClient models use <see cref="k8s.KubernetesJson"/>).
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="body">Body.</param>
        /// <param name="what">What the body is, for errors.</param>
        /// <returns>Object.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>upstream_error</c> when the body cannot be read.</exception>
        public static T Typed<T>(string body, string what) where T : class
        {
            try
            {
                T? value = k8s.KubernetesJson.Deserialize<T>(body);
                if (value == null) throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The API server returned an empty response for " + what + ".");
                return value;
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The response for " + what + " could not be read.", e);
            }
        }

        /// <summary>
        /// Shorten text for summaries.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="max">Maximum characters.</param>
        /// <returns>Shortened single-line text.</returns>
        public static string Shorten(string? value, int max)
        {
            string v = (value ?? String.Empty).Replace('\n', ' ').Replace('\r', ' ');
            return v.Length > max ? v.Substring(0, Math.Max(0, max - 3)) + "..." : v;
        }

        /// <summary>
        /// Format an object reference as <c>namespace/name</c> or <c>name</c>.
        /// </summary>
        /// <param name="ns">Namespace, or null.</param>
        /// <param name="name">Name.</param>
        /// <returns>Text.</returns>
        public static string Ref(string? ns, string name)
        {
            return String.IsNullOrEmpty(ns) ? name : ns + "/" + name;
        }

        #endregion
    }
}
