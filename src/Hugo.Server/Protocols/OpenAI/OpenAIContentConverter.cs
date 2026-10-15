namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads an OpenAI message <c>content</c>, which is either a string or an array of content parts, into plain text:
    /// the <c>text</c> of every text part, joined by newlines (image and audio parts are skipped). Writes a string.
    /// Thread-safe.
    /// </summary>
    public class OpenAIContentConverter : JsonConverter<string?>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleNull
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc />
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String) return reader.GetString();
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("content must be a string or an array of content parts.");
            StringBuilder text = new StringBuilder();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    Append(text, reader.GetString());
                    continue;
                }
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("content parts must be objects.");
                string? type = null;
                string? partText = null;
                int depth = reader.CurrentDepth;
                while (reader.Read() && !(reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth))
                {
                    if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != depth + 1) continue;
                    string? name = reader.GetString();
                    reader.Read();
                    if (name == "type" && reader.TokenType == JsonTokenType.String) type = reader.GetString();
                    else if (name == "text" && reader.TokenType == JsonTokenType.String) partText = reader.GetString();
                    else reader.Skip();
                }
                if (type == null || type == "text" || type == "input_text") Append(text, partText);
            }
            return text.ToString();
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }

        #endregion

        #region Private-Methods

        private static void Append(StringBuilder text, string? value)
        {
            if (String.IsNullOrEmpty(value)) return;
            if (text.Length > 0) text.Append('\n');
            text.Append(value);
        }

        #endregion
    }
}
