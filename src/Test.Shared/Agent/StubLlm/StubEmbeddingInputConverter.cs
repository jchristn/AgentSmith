namespace Test.Shared.Agent.StubLlm
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads the embeddings <c>input</c> field, which is a string or an array of strings, into a list.
    /// </summary>
    public class StubEmbeddingInputConverter : JsonConverter<List<string>>
    {
        /// <summary>
        /// Read.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="typeToConvert">Type.</param>
        /// <param name="options">Options.</param>
        /// <returns>Inputs.</returns>
        /// <exception cref="JsonException">Thrown for an unsupported shape.</exception>
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            List<string> result = new List<string>();
            if (reader.TokenType == JsonTokenType.String)
            {
                result.Add(reader.GetString() ?? String.Empty);
                return result;
            }
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("input must be a string or an array of strings.");
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) return result;
                if (reader.TokenType != JsonTokenType.String) throw new JsonException("input array must contain strings.");
                result.Add(reader.GetString() ?? String.Empty);
            }
            throw new JsonException("Unterminated input array.");
        }

        /// <summary>
        /// Write.
        /// </summary>
        /// <param name="writer">Writer.</param>
        /// <param name="value">Value.</param>
        /// <param name="options">Options.</param>
        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (string s in value) writer.WriteStringValue(s);
            writer.WriteEndArray();
        }
    }
}
