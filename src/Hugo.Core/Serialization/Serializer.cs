namespace Hugo.Core.Serialization
{
    using System;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Central JSON serialization: camelCase, enums as strings, nulls omitted, case-insensitive reads. Thread-safe.
    /// </summary>
    public static class Serializer
    {
        #region Public-Members

        /// <summary>
        /// Shared options.
        /// </summary>
        public static JsonSerializerOptions Options
        {
            get
            {
                return _Options;
            }
        }

        /// <summary>
        /// Options that write indented output and include nulls, used for settings files.
        /// </summary>
        public static JsonSerializerOptions FileOptions
        {
            get
            {
                return _FileOptions;
            }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = Build(false);
        private static readonly JsonSerializerOptions _FileOptions = Build(true);
        private static readonly JsonSerializerOptions _ReadableOptions = BuildReadable();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize to JSON.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        public static string Serialize(object? value)
        {
            if (value == null) return "null";
            return JsonSerializer.Serialize(value, value.GetType(), _Options);
        }

        /// <summary>
        /// Serialize to JSON with minimal escaping: quotes are written as \" and apostrophes, angle brackets, and
        /// non-ASCII characters are written as they are rather than as \uXXXX escapes. Use for JSON that people read as
        /// text inside another protocol (for example MCP tool results); never embed the output in HTML.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        public static string SerializeReadable(object? value)
        {
            if (value == null) return "null";
            return JsonSerializer.Serialize(value, value.GetType(), _ReadableOptions);
        }

        /// <summary>
        /// Deserialize JSON, returning default for empty input.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>Value or default.</returns>
        /// <exception cref="JsonException">Thrown when the JSON is malformed.</exception>
        public static T? Deserialize<T>(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return default;
            return JsonSerializer.Deserialize<T>(json, _Options);
        }

        /// <summary>
        /// Deep-copy an object through JSON.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="value">Value.</param>
        /// <returns>Copy.</returns>
        public static T? Copy<T>(T value)
        {
            if (value == null) return default;
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, _Options), _Options);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions BuildReadable()
        {
            JsonSerializerOptions options = Build(false);
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            return options;
        }

        private static JsonSerializerOptions Build(bool file)
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            // API-owned enums require names. Keep file settings and third-party enums compatible with numeric values.
            if (!file) options.Converters.Add(new StrictEnumConverterFactory("Hugo."));
            options.Converters.Add(new JsonStringEnumConverter());
            if (file)
            {
                options.WriteIndented = true;
                options.PropertyNamingPolicy = null;
            }
            else
            {
                options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            }
            return options;
        }

        #endregion
    }
}
