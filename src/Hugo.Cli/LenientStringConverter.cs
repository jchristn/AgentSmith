namespace Hugo.Cli
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a JSON string as a string and any other JSON value (number, object, array, boolean) as null, so a typed
    /// view of a loosely specified document (a JSON Schema <c>type</c> that may be an array, a <c>name</c> that may be
    /// missing) never fails the whole read. Writes strings normally. Thread-safe.
    /// </summary>
    public class LenientStringConverter : JsonConverter<string?>
    {
        /// <summary>
        /// Whether null values are passed to the converter. Always true.
        /// </summary>
        public override bool HandleNull
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Read.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="typeToConvert">Type.</param>
        /// <param name="options">Options.</param>
        /// <returns>The string, or null for any other JSON value.</returns>
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String) return reader.GetString();
            reader.Skip();
            return null;
        }

        /// <summary>
        /// Write.
        /// </summary>
        /// <param name="writer">Writer.</param>
        /// <param name="value">Value.</param>
        /// <param name="options">Options.</param>
        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }
}
