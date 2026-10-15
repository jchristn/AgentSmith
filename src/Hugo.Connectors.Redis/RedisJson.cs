namespace Hugo.Connectors.Redis
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON helpers for structured tool results. Thread-safe.
    /// </summary>
    public static class RedisJson
    {
        #region Public-Members

        /// <summary>
        /// Serializer options: camel case, nulls omitted, compact.
        /// </summary>
        public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
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
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        public static string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonSerializer.Serialize(value, value.GetType(), Options);
        }

        #endregion
    }
}
