namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Renders JSON nodes as block-style YAML, the format <c>kubectl get -o yaml</c> uses. Strings that YAML would read
    /// as another type, or that contain special characters, are double-quoted; multi-line strings use literal blocks.
    /// Thread-safe.
    /// </summary>
    public static class KubernetesYamlWriter
    {
        #region Private-Members

        private static readonly Regex _Plain = new Regex("^[A-Za-z0-9_./@$()+-][A-Za-z0-9 _./@$()+=,:-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly JsonSerializerOptions _QuoteOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private static readonly Regex _Ambiguous = new Regex("^(?:[-+]?(?:[0-9][0-9_]*)(?:\\.[0-9_]*)?(?:[eE][-+]?[0-9]+)?|0x[0-9a-fA-F]+|0o[0-7]+|\\.inf|-\\.inf|\\.nan|y|Y|yes|Yes|YES|n|N|no|No|NO|true|True|TRUE|false|False|FALSE|on|On|ON|off|Off|OFF|null|Null|NULL|~|[0-9]{4}-[0-9]{2}-[0-9]{2}.*)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render a node as YAML.
        /// </summary>
        /// <param name="node">Node, or null.</param>
        /// <returns>YAML text ending with a newline.</returns>
        public static string Write(JsonNode? node)
        {
            StringBuilder sb = new StringBuilder();
            if (node is JsonObject obj && obj.Count > 0) WriteObject(sb, obj, 0);
            else if (node is JsonArray arr && arr.Count > 0) WriteArray(sb, arr, 0);
            else sb.Append(Scalar(node)).Append('\n');
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static void WriteObject(StringBuilder sb, JsonObject obj, int indent)
        {
            foreach (System.Collections.Generic.KeyValuePair<string, JsonNode?> kv in obj)
            {
                sb.Append(' ', indent).Append(Key(kv.Key)).Append(':');
                WriteValue(sb, kv.Value, indent);
            }
        }

        private static void WriteArray(StringBuilder sb, JsonArray arr, int indent)
        {
            foreach (JsonNode? item in arr)
            {
                sb.Append(' ', indent).Append('-');
                if (item is JsonObject o && o.Count > 0)
                {
                    bool first = true;
                    foreach (System.Collections.Generic.KeyValuePair<string, JsonNode?> kv in o)
                    {
                        if (first)
                        {
                            sb.Append(' ').Append(Key(kv.Key)).Append(':');
                            first = false;
                        }
                        else
                        {
                            sb.Append(' ', indent + 2).Append(Key(kv.Key)).Append(':');
                        }
                        WriteValue(sb, kv.Value, indent + 2);
                    }
                }
                else if (item is JsonArray a && a.Count > 0)
                {
                    sb.Append('\n');
                    WriteArray(sb, a, indent + 2);
                }
                else
                {
                    WriteValue(sb, item, indent);
                }
            }
        }

        private static void WriteValue(StringBuilder sb, JsonNode? value, int indent)
        {
            if (value is JsonObject o)
            {
                if (o.Count == 0)
                {
                    sb.Append(" {}\n");
                    return;
                }
                sb.Append('\n');
                WriteObject(sb, o, indent + 2);
                return;
            }
            if (value is JsonArray a)
            {
                if (a.Count == 0)
                {
                    sb.Append(" []\n");
                    return;
                }
                sb.Append('\n');
                WriteArray(sb, a, indent);
                return;
            }
            string? text = StringValue(value);
            if (text != null && text.IndexOf('\n') >= 0 && text.IndexOf('\r') < 0 && !text.StartsWith(' ') && !text.Contains("\t", StringComparison.Ordinal))
            {
                string body = text.EndsWith('\n') ? text.Substring(0, text.Length - 1) : text;
                sb.Append(text.EndsWith('\n') ? " |\n" : " |-\n");
                foreach (string line in body.Split('\n'))
                {
                    if (line.Length > 0) sb.Append(' ', indent + 2).Append(line);
                    sb.Append('\n');
                }
                return;
            }
            sb.Append(' ').Append(Scalar(value)).Append('\n');
        }

        private static string Key(string key)
        {
            if (key.Length > 0 && _Plain.IsMatch(key) && !_Ambiguous.IsMatch(key) && !key.Contains(": ", StringComparison.Ordinal) && !key.EndsWith(':')) return key;
            return JsonSerializer.Serialize(key);
        }

        private static string Scalar(JsonNode? value)
        {
            if (value == null) return "null";
            switch (value.GetValueKind())
            {
                case JsonValueKind.String:
                    return QuoteIfNeeded(value.GetValue<string>());
                case JsonValueKind.True:
                    return "true";
                case JsonValueKind.False:
                    return "false";
                case JsonValueKind.Null:
                    return "null";
                default:
                    return value.ToJsonString();
            }
        }

        private static string? StringValue(JsonNode? value)
        {
            if (value is JsonValue && value.GetValueKind() == JsonValueKind.String) return value.GetValue<string>();
            return null;
        }

        private static string QuoteIfNeeded(string s)
        {
            if (s.Length == 0) return "\"\"";
            bool plain = _Plain.IsMatch(s)
                && !_Ambiguous.IsMatch(s)
                && !s.EndsWith(' ')
                && !s.EndsWith(':')
                && !s.Contains(": ", StringComparison.Ordinal)
                && !s.Contains(" #", StringComparison.Ordinal)
                && !(s[0] == '-' && (s.Length == 1 || s[1] == ' '));
            if (plain) return s;
            return JsonSerializer.Serialize(s, _QuoteOptions);
        }

        #endregion
    }
}
