namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads the JSON Schema "type" keyword, which is either one string or an array of strings. Thread-safe.
    /// </summary>
    public class SchemaTypeListConverter : JsonConverter<List<string>>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            List<string> types = new List<string>();
            if (reader.TokenType == JsonTokenType.String)
            {
                string? single = reader.GetString();
                if (!String.IsNullOrEmpty(single)) types.Add(single);
                return types;
            }
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        string? item = reader.GetString();
                        if (!String.IsNullOrEmpty(item)) types.Add(item);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                return types;
            }
            reader.Skip();
            return types;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            if (value == null || value.Count == 0)
            {
                writer.WriteNullValue();
                return;
            }
            if (value.Count == 1)
            {
                writer.WriteStringValue(value[0]);
                return;
            }
            writer.WriteStartArray();
            foreach (string item in value) writer.WriteStringValue(item);
            writer.WriteEndArray();
        }

        #endregion
    }
}
