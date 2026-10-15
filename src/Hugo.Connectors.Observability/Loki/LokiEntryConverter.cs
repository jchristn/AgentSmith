namespace Hugo.Connectors.Observability
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads and writes <see cref="LokiEntry"/> as <c>["unixNanoseconds", "line"]</c>. Thread-safe.
    /// </summary>
    public class LokiEntryConverter : JsonConverter<LokiEntry>
    {
        /// <inheritdoc />
        /// <exception cref="JsonException">Thrown when the token is not an entry array.</exception>
        public override LokiEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A Loki entry must be an array.");
            LokiEntry entry = new LokiEntry();
            reader.Read();
            if (reader.TokenType == JsonTokenType.String) entry.Timestamp = reader.GetString() ?? "0";
            else if (reader.TokenType == JsonTokenType.Number) entry.Timestamp = reader.GetDecimal().ToString("0", CultureInfo.InvariantCulture);
            else throw new JsonException("A Loki entry timestamp must be a string.");
            reader.Read();
            if (reader.TokenType == JsonTokenType.String) entry.Line = reader.GetString() ?? String.Empty;
            else throw new JsonException("A Loki entry line must be a string.");
            reader.Read();
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                reader.Skip();
                reader.Read();
            }
            return entry;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, LokiEntry value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteStartArray();
            writer.WriteStringValue(value.Timestamp);
            writer.WriteStringValue(value.Line);
            writer.WriteEndArray();
        }
    }
}
