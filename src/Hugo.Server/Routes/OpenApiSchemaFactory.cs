namespace Hugo.Server.Routes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json.Serialization;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Builds OpenAPI schemas from C# types by reflection (camelCase properties, enums as string lists, depth-limited).
    /// Used so the API Explorer can template request bodies. Thread-safe.
    /// </summary>
    public static class OpenApiSchemaFactory
    {
        #region Public-Members

        /// <summary>
        /// Maximum nesting depth. Minimum 1, maximum 8, default 3.
        /// </summary>
        public static int MaxDepth
        {
            get
            {
                return _MaxDepth;
            }
            set
            {
                _MaxDepth = Math.Clamp(value, 1, 8);
            }
        }

        #endregion

        #region Private-Members

        private static int _MaxDepth = 3;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a schema for a type.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>Schema.</returns>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        public static OpenApiSchemaMetadata For(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return Build(type, 0);
        }

        #endregion

        #region Private-Methods

        private static OpenApiSchemaMetadata Build(Type type, int depth)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;

            if (type == typeof(string)) return OpenApiSchemaMetadata.String();
            if (type == typeof(bool)) return OpenApiSchemaMetadata.Boolean();
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return OpenApiSchemaMetadata.Integer(type == typeof(long) ? "int64" : "int32");
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return OpenApiSchemaMetadata.Number("double");
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return OpenApiSchemaMetadata.String("date-time");
            if (type.IsEnum)
            {
                OpenApiSchemaMetadata e = OpenApiSchemaMetadata.String();
                e.Enum = Enum.GetNames(type).Cast<object>().ToList();
                return e;
            }

            if (type != typeof(string) && typeof(IDictionary).IsAssignableFrom(type))
            {
                return OpenApiSchemaMetadata.Create("object", null!);
            }

            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
            {
                Type element = type.IsArray ? type.GetElementType()! : (type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object));
                return OpenApiSchemaMetadata.CreateArray(depth >= _MaxDepth ? OpenApiSchemaMetadata.Create("object", null!) : Build(element, depth + 1));
            }

            OpenApiSchemaMetadata schema = OpenApiSchemaMetadata.Create("object", null!);
            if (depth >= _MaxDepth) return schema;
            schema.Properties = new Dictionary<string, OpenApiSchemaMetadata>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                string name = Char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
                schema.Properties[name] = Build(property.PropertyType, depth + 1);
            }
            return schema;
        }

        #endregion
    }
}
