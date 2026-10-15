namespace Hugo.Cli
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON settings and helpers shared by the CLI. Thread-safe.
    /// </summary>
    public static class CliJson
    {
        #region Public-Members

        /// <summary>
        /// Options for API payloads and the credential file: camelCase names, case-insensitive reads, string enums,
        /// nulls omitted on write.
        /// </summary>
        public static JsonSerializerOptions Options
        {
            get
            {
                return _Options;
            }
        }

        /// <summary>
        /// The same as <see cref="Options"/> but indented, for files and <c>--json</c> output.
        /// </summary>
        public static JsonSerializerOptions Indented
        {
            get
            {
                return _Indented;
            }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = Build(false);
        private static readonly JsonSerializerOptions _Indented = Build(true);
        private static readonly JsonWriterOptions _CompactWriter = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rewrite a JSON text on one line without insignificant whitespace. Line breaks inside strings stay escaped,
        /// so the result never contains a raw newline (the MCP stdio framing requirement).
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Compact JSON.</returns>
        /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
        /// <exception cref="JsonException">Thrown when the text is not valid JSON.</exception>
        public static string Minify(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, _CompactWriter))
                    {
                        document.WriteTo(writer);
                    }
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }

        /// <summary>
        /// Deserialize, returning null instead of throwing for invalid or mismatched JSON.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text, or null.</param>
        /// <returns>Object or null.</returns>
        public static T? TryDeserialize<T>(string? json) where T : class
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(json, _Options);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>
        /// Deserialize an opaque JSON value, returning null instead of throwing for invalid or mismatched JSON.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="value">JSON value, or null.</param>
        /// <returns>Object or null.</returns>
        public static T? TryDeserialize<T>(JsonElement? value) where T : class
        {
            if (value == null || value.Value.ValueKind != JsonValueKind.Object) return null;
            try
            {
                return value.Value.Deserialize<T>(_Options);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions Build(bool indented)
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            options.WriteIndented = indented;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
