namespace Hugo.Connectors.Observability
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads and writes <see cref="PromPoint"/> as the two-element array <c>[unixSeconds, "value"]</c>. Thread-safe.
    /// </summary>
    public class PromPointConverter : JsonConverter<PromPoint>
    {
        /// <inheritdoc />
        /// <exception cref="JsonException">Thrown when the token is not a two-element array.</exception>
        public override PromPoint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A Prometheus sample must be an array.");
            PromPoint point = new PromPoint();
            reader.Read();
            if (reader.TokenType == JsonTokenType.Number) point.Timestamp = reader.GetDouble();
            else if (reader.TokenType == JsonTokenType.String && Double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ts)) point.Timestamp = ts;
            else throw new JsonException("A Prometheus sample timestamp must be a number.");
            reader.Read();
            if (reader.TokenType == JsonTokenType.String) point.Value = reader.GetString() ?? String.Empty;
            else if (reader.TokenType == JsonTokenType.Number) point.Value = reader.GetDouble().ToString("R", CultureInfo.InvariantCulture);
            else throw new JsonException("A Prometheus sample value must be a string.");
            reader.Read();
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                reader.Skip();
                reader.Read();
            }
            return point;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, PromPoint value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteStartArray();
            writer.WriteNumberValue(value.Timestamp);
            writer.WriteStringValue(value.Value);
            writer.WriteEndArray();
        }
    }
}
