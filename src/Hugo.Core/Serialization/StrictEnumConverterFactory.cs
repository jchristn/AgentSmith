namespace Hugo.Core.Serialization
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Converts named enum values only, rejecting numeric and undefined values. Immutable and thread-safe.
    /// This is a Hugo implementation: no matching helper exists in the recorded Garrison fork source (P0.16).
    /// </summary>
    public sealed class StrictEnumConverterFactory : JsonConverterFactory
    {
        private readonly string? _NamespacePrefix;

        /// <summary>
        /// Instantiate a converter factory.
        /// </summary>
        /// <param name="namespacePrefix">Optional namespace prefix restricting converted enum types; null handles all enums.</param>
        public StrictEnumConverterFactory(string? namespacePrefix = null)
        {
            _NamespacePrefix = namespacePrefix;
        }

        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsEnum && (_NamespacePrefix == null || (typeToConvert.Namespace?.StartsWith(_NamespacePrefix, StringComparison.Ordinal) ?? false));
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Thrown when the type is outside this factory's enum scope.</exception>
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            if (!CanConvert(typeToConvert)) throw new ArgumentException("The type is not an enum in this converter's namespace scope.", nameof(typeToConvert));
            Type converterType = typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter)(Activator.CreateInstance(converterType) ?? throw new InvalidOperationException("Could not instantiate the strict enum converter."));
        }
    }
}
