namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Validates tool arguments against a tool's input schema (P3.10, plan section 8.6 step 4) and finds the primary
    /// command argument of a per-call classified tool. Supports the subset described on <see cref="ToolInputSchema"/>.
    /// Parsed schemas are cached by their text. Thread-safe.
    /// </summary>
    public static class ToolArgumentValidator
    {
        #region Public-Members

        /// <summary>
        /// Argument names that name the primary command argument by convention, in order of preference, when no
        /// property is marked x-hugo-primary.
        /// </summary>
        public static IReadOnlyList<string> PrimaryArgumentNames
        {
            get
            {
                return _PrimaryNames;
            }
        }

        /// <summary>
        /// Timeout for pattern keywords. Default 250 ms.
        /// </summary>
        public static TimeSpan PatternTimeout { get; set; } = TimeSpan.FromMilliseconds(250);

        #endregion

        #region Private-Members

        private static readonly List<string> _PrimaryNames = new List<string> { "command", "sql", "statement", "query", "script", "expression" };
        private static readonly ConcurrentDictionary<string, ToolInputSchema> _Cache = new ConcurrentDictionary<string, ToolInputSchema>(StringComparer.Ordinal);
        private static readonly int _MaxErrors = 20;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a schema (cached).
        /// </summary>
        /// <param name="schemaJson">Schema JSON.</param>
        /// <returns>Schema. An unparseable schema yields an empty schema that accepts any object.</returns>
        public static ToolInputSchema Parse(string? schemaJson)
        {
            string text = String.IsNullOrWhiteSpace(schemaJson) ? "{\"type\":\"object\"}" : schemaJson;
            if (_Cache.TryGetValue(text, out ToolInputSchema? cached)) return cached;
            ToolInputSchema schema;
            try
            {
                schema = JsonSerializer.Deserialize<ToolInputSchema>(text) ?? new ToolInputSchema();
            }
            catch (JsonException)
            {
                schema = new ToolInputSchema();
            }
            if (_Cache.Count > 4096) _Cache.Clear();
            _Cache[text] = schema;
            return schema;
        }

        /// <summary>
        /// Validate arguments against a schema.
        /// </summary>
        /// <param name="schemaJson">Tool input schema JSON.</param>
        /// <param name="argumentsJson">Arguments JSON object.</param>
        /// <param name="ignoredProperties">Properties removed before validation (for example the reserved resource argument).</param>
        /// <returns>Messages, empty when valid. Never null.</returns>
        public static List<string> Validate(string? schemaJson, string? argumentsJson, params string[] ignoredProperties)
        {
            List<string> errors = new List<string>();
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(String.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            }
            catch (JsonException e)
            {
                errors.Add("Arguments are not valid JSON: " + e.Message);
                return errors;
            }
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("Arguments must be a JSON object.");
                    return errors;
                }
                HashSet<string> ignored = new HashSet<string>(ignoredProperties ?? new string[0], StringComparer.Ordinal);
                ValidateObject(Parse(schemaJson), document.RootElement, "$", ignored, errors);
            }
            return errors;
        }

        /// <summary>
        /// The primary command argument of a tool: the property marked x-hugo-primary, else the first property named in
        /// <see cref="PrimaryArgumentNames"/>, else the first required string property. Null when the schema has none.
        /// </summary>
        /// <param name="schemaJson">Tool input schema JSON.</param>
        /// <returns>Property name or null.</returns>
        public static string? PrimaryArgument(string? schemaJson)
        {
            ToolInputSchema schema = Parse(schemaJson);
            if (schema.Properties == null || schema.Properties.Count == 0) return null;
            KeyValuePair<string, ToolInputSchema> marked = schema.Properties.FirstOrDefault(p => p.Value != null && p.Value.HugoPrimary == true);
            if (!String.IsNullOrEmpty(marked.Key)) return marked.Key;
            foreach (string name in _PrimaryNames)
            {
                if (schema.Properties.ContainsKey(name)) return name;
            }
            if (schema.Required != null)
            {
                foreach (string name in schema.Required)
                {
                    if (schema.Properties.TryGetValue(name, out ToolInputSchema? property) && property != null && (property.Type.Count == 0 || property.Type.Contains("string"))) return name;
                }
            }
            return null;
        }

        #endregion

        #region Private-Methods

        private static void ValidateObject(ToolInputSchema schema, JsonElement value, string path, HashSet<string> ignored, List<string> errors)
        {
            Dictionary<string, ToolInputSchema> properties = schema.Properties ?? new Dictionary<string, ToolInputSchema>(StringComparer.Ordinal);
            if (schema.Required != null)
            {
                foreach (string name in schema.Required)
                {
                    if (ignored.Contains(name)) continue;
                    if (!value.TryGetProperty(name, out JsonElement present) || present.ValueKind == JsonValueKind.Null) Add(errors, path + "." + name + " is required.");
                }
            }
            bool closed = schema.AdditionalProperties.HasValue && schema.AdditionalProperties.Value.ValueKind == JsonValueKind.False;
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (ignored.Contains(property.Name)) continue;
                if (properties.TryGetValue(property.Name, out ToolInputSchema? child) && child != null)
                {
                    ValidateValue(child, property.Value, path + "." + property.Name, errors);
                }
                else if (closed)
                {
                    Add(errors, path + "." + property.Name + " is not a recognized argument.");
                }
            }
        }

        private static void ValidateValue(ToolInputSchema schema, JsonElement value, string path, List<string> errors)
        {
            if (errors.Count >= _MaxErrors) return;
            if (value.ValueKind == JsonValueKind.Null && (schema.Type.Count == 0 || schema.Type.Contains("null"))) return;
            if (schema.Type.Count > 0 && !schema.Type.Any(t => Matches(t, value)))
            {
                Add(errors, path + " must be " + String.Join(" or ", schema.Type) + ".");
                return;
            }
            if (schema.Enum != null && schema.Enum.Count > 0)
            {
                string raw = value.GetRawText();
                if (!schema.Enum.Any(e => String.Equals(e.GetRawText(), raw, StringComparison.Ordinal)))
                {
                    Add(errors, path + " must be one of " + String.Join(", ", schema.Enum.Select(e => e.GetRawText())) + ".");
                    return;
                }
            }
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    string text = value.GetString() ?? String.Empty;
                    if (schema.MinLength.HasValue && text.Length < schema.MinLength.Value) Add(errors, path + " must be at least " + schema.MinLength.Value + " characters.");
                    if (schema.MaxLength.HasValue && text.Length > schema.MaxLength.Value) Add(errors, path + " must be at most " + schema.MaxLength.Value + " characters.");
                    if (!String.IsNullOrEmpty(schema.Pattern))
                    {
                        try
                        {
                            if (!Regex.IsMatch(text, schema.Pattern, RegexOptions.CultureInvariant, PatternTimeout)) Add(errors, path + " does not match the required pattern.");
                        }
                        catch (Exception e) when (e is ArgumentException || e is RegexMatchTimeoutException)
                        {
                            Add(errors, path + " could not be checked against its pattern.");
                        }
                    }
                    break;
                case JsonValueKind.Number:
                    double number = value.GetDouble();
                    if (schema.Minimum.HasValue && number < schema.Minimum.Value) Add(errors, path + " must be at least " + schema.Minimum.Value.ToString(CultureInfo.InvariantCulture) + ".");
                    if (schema.Maximum.HasValue && number > schema.Maximum.Value) Add(errors, path + " must be at most " + schema.Maximum.Value.ToString(CultureInfo.InvariantCulture) + ".");
                    break;
                case JsonValueKind.Array:
                    int count = value.GetArrayLength();
                    if (schema.MinItems.HasValue && count < schema.MinItems.Value) Add(errors, path + " must have at least " + schema.MinItems.Value + " items.");
                    if (schema.MaxItems.HasValue && count > schema.MaxItems.Value) Add(errors, path + " must have at most " + schema.MaxItems.Value + " items.");
                    if (schema.Items != null)
                    {
                        int index = 0;
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            ValidateValue(schema.Items, item, path + "[" + index + "]", errors);
                            index++;
                        }
                    }
                    break;
                case JsonValueKind.Object:
                    if (schema.Properties != null || schema.Required != null || schema.AdditionalProperties.HasValue) ValidateObject(schema, value, path, new HashSet<string>(StringComparer.Ordinal), errors);
                    break;
            }
        }

        private static bool Matches(string type, JsonElement value)
        {
            switch (type)
            {
                case "string":
                    return value.ValueKind == JsonValueKind.String;
                case "integer":
                    return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long _);
                case "number":
                    return value.ValueKind == JsonValueKind.Number;
                case "boolean":
                    return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
                case "array":
                    return value.ValueKind == JsonValueKind.Array;
                case "object":
                    return value.ValueKind == JsonValueKind.Object;
                case "null":
                    return value.ValueKind == JsonValueKind.Null;
                default:
                    return true;
            }
        }

        private static void Add(List<string> errors, string message)
        {
            if (errors.Count < _MaxErrors) errors.Add(message);
        }

        #endregion
    }
}
