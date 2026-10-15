namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;

    /// <summary>
    /// Truthiness, stringification, and JSON and TOML encoding for context values. Thread-safe.
    /// </summary>
    public static class TemplateValues
    {
        #region Public-Methods

        /// <summary>
        /// Truthiness: null, false, empty strings, "false", zero, and empty collections are false.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when truthy.</returns>
        public static bool IsTruthy(object? value)
        {
            switch (value)
            {
                case null:
                    return false;
                case bool b:
                    return b;
                case string s:
                    return s.Trim().Length > 0 && !s.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                case int i:
                    return i != 0;
                case long l:
                    return l != 0;
                case double d:
                    return d != 0;
                case List<object?> list:
                    return list.Count > 0;
                case Dictionary<string, object?> map:
                    return map.Count > 0;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Whether a value is empty for the default filter (null or empty string).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when empty.</returns>
        public static bool IsEmpty(object? value)
        {
            if (value == null) return true;
            if (value is string s) return s.Length == 0;
            if (value is List<object?> list) return list.Count == 0;
            return false;
        }

        /// <summary>
        /// Render a value as text.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Text.</returns>
        public static string ToText(object? value)
        {
            switch (value)
            {
                case null:
                    return String.Empty;
                case string s:
                    return s;
                case bool b:
                    return b ? "true" : "false";
                case DateTime dt:
                    return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                case double d:
                    return d.ToString(CultureInfo.InvariantCulture);
                case IFormattable f:
                    return f.ToString(null, CultureInfo.InvariantCulture);
                case List<object?> list:
                    List<string> items = new List<string>();
                    foreach (object? item in list) items.Add(ToText(item));
                    return String.Join(", ", items);
                case Dictionary<string, object?>:
                    return ToJson(value);
                default:
                    return value.ToString() ?? String.Empty;
            }
        }

        /// <summary>
        /// Encode a value as compact JSON.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON.</returns>
        public static string ToJson(object? value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                JsonWriterOptions options = new JsonWriterOptions();
                options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, options))
                {
                    WriteJson(writer, value);
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// Encode a value as a TOML value: basic strings, booleans, numbers, arrays, and inline tables.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>TOML text.</returns>
        public static string ToToml(object? value)
        {
            switch (value)
            {
                case null:
                    return "\"\"";
                case bool b:
                    return b ? "true" : "false";
                case int:
                case long:
                case double:
                    return ToText(value);
                case List<object?> list:
                    List<string> items = new List<string>();
                    foreach (object? item in list) items.Add(ToToml(item));
                    return "[" + String.Join(", ", items) + "]";
                case Dictionary<string, object?> map:
                    List<string> pairs = new List<string>();
                    foreach (KeyValuePair<string, object?> entry in map) pairs.Add(TomlString(entry.Key) + " = " + ToToml(entry.Value));
                    return "{ " + String.Join(", ", pairs) + " }";
                default:
                    return TomlString(ToText(value));
            }
        }

        #endregion

        #region Private-Methods

        private static string TomlString(string text)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static void WriteJson(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case long l:
                    writer.WriteNumberValue(l);
                    break;
                case double d:
                    writer.WriteNumberValue(d);
                    break;
                case DateTime:
                    writer.WriteStringValue(ToText(value));
                    break;
                case List<object?> list:
                    writer.WriteStartArray();
                    foreach (object? item in list) WriteJson(writer, item);
                    writer.WriteEndArray();
                    break;
                case Dictionary<string, object?> map:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, object?> entry in map)
                    {
                        writer.WritePropertyName(entry.Key);
                        WriteJson(writer, entry.Value);
                    }
                    writer.WriteEndObject();
                    break;
                default:
                    writer.WriteStringValue(ToText(value));
                    break;
            }
        }

        #endregion
    }
}
