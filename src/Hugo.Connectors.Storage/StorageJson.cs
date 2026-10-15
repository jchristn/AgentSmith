namespace Hugo.Connectors.Storage
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON serialization for structured tool results (camelCase, nulls omitted). Thread-safe.
    /// </summary>
    public static class StorageJson
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a value.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _Options);
        }

        /// <summary>
        /// Deserialize a value.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>Value, or default when the text is null.</returns>
        /// <exception cref="JsonException">Thrown when the text is not valid JSON for the type.</exception>
        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _Options);
        }

        #endregion
    }
}
