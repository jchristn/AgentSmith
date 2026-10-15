namespace Hugo.Connectors.Observability
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads <see cref="EsTotalHits"/> from an object or a number, and writes it as an object. Thread-safe.
    /// </summary>
    public class EsTotalHitsConverter : JsonConverter<EsTotalHits>
    {
        /// <inheritdoc />
        /// <exception cref="JsonException">Thrown for another token type.</exception>
        public override EsTotalHits? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            EsTotalHits total = new EsTotalHits();
            if (reader.TokenType == JsonTokenType.Number)
            {
                total.Value = reader.GetInt64();
                return total;
            }
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("hits.total must be an object or a number.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString() ?? String.Empty;
                reader.Read();
                if (name == "value" && reader.TokenType == JsonTokenType.Number) total.Value = reader.GetInt64();
                else if (name == "relation" && reader.TokenType == JsonTokenType.String) total.Relation = reader.GetString() ?? "eq";
                else reader.Skip();
            }
            return total;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, EsTotalHits value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteStartObject();
            writer.WriteNumber("value", value.Value);
            writer.WriteString("relation", value.Relation);
            writer.WriteEndObject();
        }
    }
}
