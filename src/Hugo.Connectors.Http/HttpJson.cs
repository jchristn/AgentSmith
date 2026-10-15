namespace Hugo.Connectors.Http
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON helpers for the <c>http</c> connector. Thread-safe.
    /// </summary>
    public static class HttpJson
    {
        /// <summary>
        /// Serializer options: camelCase, nulls omitted, compact.
        /// </summary>
        public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// Serialize a value.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON.</returns>
        public static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, value == null ? typeof(object) : value.GetType(), Options);
        }
    }
}
