namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Arguments of one tool call, already validated against the tool's input schema by the server. Tool arguments are
    /// schemaless at the framework level (each tool defines its own), so this class reads values from the parsed JSON
    /// object with typed getters. Thread-safe for reads.
    /// </summary>
    public class ToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The arguments as a JSON object string.
        /// </summary>
        public string Json { get; }

        #endregion

        #region Private-Members

        private readonly Dictionary<string, JsonElement> _Values;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate from a JSON object string. Null or empty means no arguments.
        /// </summary>
        /// <param name="json">JSON object.</param>
        /// <exception cref="ArgumentException">Thrown when the JSON is not an object.</exception>
        public ToolArguments(string? json)
        {
            Json = String.IsNullOrWhiteSpace(json) ? "{}" : json;
            _Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(Json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Tool arguments must be a JSON object.", nameof(json));
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject()) _Values[prop.Name] = prop.Value.Clone();
                }
            }
            catch (JsonException e)
            {
                throw new ArgumentException("Tool arguments are not valid JSON: " + e.Message, nameof(json), e);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a non-null value exists.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <returns>True when present.</returns>
        public bool Has(string name)
        {
            return _Values.TryGetValue(name, out JsonElement e) && e.ValueKind != JsonValueKind.Null && e.ValueKind != JsonValueKind.Undefined;
        }

        /// <summary>
        /// Argument names present.
        /// </summary>
        /// <returns>Names.</returns>
        public IReadOnlyCollection<string> Names()
        {
            return _Values.Keys;
        }

        /// <summary>
        /// Read a string argument. Numbers and booleans are converted to their invariant text.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">Value returned when absent.</param>
        /// <returns>Value or fallback.</returns>
        public string? GetString(string name, string? fallback = null)
        {
            if (!_Values.TryGetValue(name, out JsonElement e)) return fallback;
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    return e.GetString();
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return e.GetRawText();
                default:
                    return fallback;
            }
        }

        /// <summary>
        /// Read a required string argument.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <returns>Value.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_argument</c> when absent or empty.</exception>
        public string GetRequiredString(string name)
        {
            string? value = GetString(name);
            if (String.IsNullOrEmpty(value)) throw new ConnectorException("invalid_argument", "Argument '" + name + "' is required.");
            return value;
        }

        /// <summary>
        /// Read an integer argument, clamped to a range.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">Value returned when absent or not an integer.</param>
        /// <param name="minimum">Minimum.</param>
        /// <param name="maximum">Maximum.</param>
        /// <returns>Value.</returns>
        public long GetLong(string name, long fallback, long minimum = Int64.MinValue, long maximum = Int64.MaxValue)
        {
            long value = fallback;
            if (_Values.TryGetValue(name, out JsonElement e))
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long n)) value = n;
                else if (e.ValueKind == JsonValueKind.String && Int64.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s)) value = s;
            }
            if (value < minimum) value = minimum;
            if (value > maximum) value = maximum;
            return value;
        }

        /// <summary>
        /// Read an integer argument as <see cref="int"/>, clamped to a range.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">Value returned when absent.</param>
        /// <param name="minimum">Minimum.</param>
        /// <param name="maximum">Maximum.</param>
        /// <returns>Value.</returns>
        public int GetInt(string name, int fallback, int minimum = Int32.MinValue, int maximum = Int32.MaxValue)
        {
            return (int)GetLong(name, fallback, minimum, maximum);
        }

        /// <summary>
        /// Read a boolean argument.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">Value returned when absent.</param>
        /// <returns>Value.</returns>
        public bool GetBool(string name, bool fallback)
        {
            if (!_Values.TryGetValue(name, out JsonElement e)) return fallback;
            if (e.ValueKind == JsonValueKind.True) return true;
            if (e.ValueKind == JsonValueKind.False) return false;
            if (e.ValueKind == JsonValueKind.String)
            {
                string v = (e.GetString() ?? String.Empty).Trim().ToLowerInvariant();
                if (v == "true" || v == "1" || v == "yes") return true;
                if (v == "false" || v == "0" || v == "no") return false;
            }
            return fallback;
        }

        /// <summary>
        /// Read a string array argument. A single string becomes a one-element list.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <returns>Values; empty when absent.</returns>
        public List<string> GetStringList(string name)
        {
            List<string> list = new List<string>();
            if (!_Values.TryGetValue(name, out JsonElement e)) return list;
            if (e.ValueKind == JsonValueKind.String)
            {
                string? s = e.GetString();
                if (!String.IsNullOrEmpty(s)) list.Add(s);
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in e.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? String.Empty);
                    else if (item.ValueKind != JsonValueKind.Null) list.Add(item.GetRawText());
                }
            }
            return list;
        }

        /// <summary>
        /// Read a nested object or array argument as raw JSON text (for provider-specific options or request bodies).
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <returns>JSON text, or null when absent.</returns>
        public string? GetRawJson(string name)
        {
            if (!_Values.TryGetValue(name, out JsonElement e)) return null;
            if (e.ValueKind == JsonValueKind.Null || e.ValueKind == JsonValueKind.Undefined) return null;
            return e.GetRawText();
        }

        /// <summary>
        /// Read a nested object of string values (for example HTTP headers). Non-string values use their raw text.
        /// </summary>
        /// <param name="name">Argument name.</param>
        /// <returns>Map; empty when absent or not an object.</returns>
        public Dictionary<string, string> GetStringMap(string name)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!_Values.TryGetValue(name, out JsonElement e) || e.ValueKind != JsonValueKind.Object) return map;
            foreach (JsonProperty prop in e.EnumerateObject())
                map[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? String.Empty) : prop.Value.GetRawText();
            return map;
        }

        #endregion
    }
}
