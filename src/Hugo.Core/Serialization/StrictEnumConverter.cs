namespace Hugo.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads case-insensitive declared enum names and writes their canonical names. Immutable and thread-safe.
    /// Numeric tokens, numeric strings, undefined values, and undeclared flag combinations are rejected.
    /// </summary>
    /// <typeparam name="TEnum">Enum type.</typeparam>
    public sealed class StrictEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> _Names = BuildNames();

        /// <inheritdoc />
        /// <exception cref="JsonException">Thrown when input is not a declared enum name.</exception>
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? name = reader.GetString();
                if (name != null && _Names.TryGetValue(name, out TEnum value)) return value;
            }
            throw new JsonException("Expected a declared name for enum " + typeof(TEnum).Name + ".");
        }

        /// <inheritdoc />
        /// <exception cref="JsonException">Thrown when the value has no declared name.</exception>
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            string? name = Enum.GetName(value);
            if (name == null) throw new JsonException("Cannot serialize an undefined value of enum " + typeof(TEnum).Name + ".");
            writer.WriteStringValue(name);
        }

        private static Dictionary<string, TEnum> BuildNames()
        {
            Dictionary<string, TEnum> names = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in Enum.GetNames<TEnum>()) names[name] = Enum.Parse<TEnum>(name);
            return names;
        }
    }
}
