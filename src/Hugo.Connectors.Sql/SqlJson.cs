namespace Hugo.Connectors.Sql
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON serialization for structured tool content: camelCase names, null properties omitted, enums as names. Thread-safe.
    /// </summary>
    public static class SqlJson
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a value.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        public static string Serialize<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return JsonSerializer.Serialize(value, _Options);
        }

        /// <summary>
        /// Deserialize a value.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>Value, or null for JSON null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
        /// <exception cref="JsonException">Thrown for invalid JSON.</exception>
        public static T? Deserialize<T>(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return JsonSerializer.Deserialize<T>(json, _Options);
        }

        #endregion
    }
}
