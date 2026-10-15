namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;
    using YamlDotNet.Core;
    using YamlDotNet.RepresentationModel;

    /// <summary>
    /// Parses manifests (YAML with one or more documents, or JSON) into JSON objects for server-side apply. Lists
    /// (<c>kind: List</c> or a JSON array) are expanded. Plain YAML scalars follow the YAML 1.2 core schema (booleans,
    /// integers, floats, null); quoted scalars stay strings. Thread-safe.
    /// </summary>
    public static class KubernetesManifestParser
    {
        #region Public-Members

        /// <summary>
        /// Largest manifest accepted, in bytes (1 MiB).
        /// </summary>
        public static int MaxManifestBytes { get; } = 1024 * 1024;

        /// <summary>
        /// Most documents accepted in one manifest (50).
        /// </summary>
        public static int MaxDocuments { get; } = 50;

        #endregion

        #region Private-Members

        private static readonly Regex _Int = new Regex("^[-+]?[0-9]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex _Float = new Regex("^[-+]?(\\.[0-9]+|[0-9]+(\\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a manifest.
        /// </summary>
        /// <param name="text">YAML or JSON text.</param>
        /// <returns>Objects, in order. Empty documents are skipped.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the text cannot be parsed, has no
        /// objects, or has too many, and with <c>too_large</c> when it exceeds <see cref="MaxManifestBytes"/>.</exception>
        public static List<JsonObject> Parse(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The manifest is empty.");
            if (Encoding.UTF8.GetByteCount(text) > MaxManifestBytes) throw new ConnectorException(ConnectorErrorCodes.TooLarge, "The manifest is larger than " + MaxManifestBytes + " bytes. Apply it in parts.");

            List<JsonNode?> roots = new List<JsonNode?>();
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    roots.Add(JsonNode.Parse(text));
                }
                catch (JsonException e)
                {
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The manifest is not valid JSON: " + e.Message, e);
                }
            }
            else
            {
                YamlStream stream = new YamlStream();
                try
                {
                    using (StringReader reader = new StringReader(text))
                    {
                        stream.Load(reader);
                    }
                }
                catch (YamlException e)
                {
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The manifest is not valid YAML (line " + e.Start.Line + "): " + e.Message, e);
                }
                foreach (YamlDocument doc in stream.Documents) roots.Add(Convert(doc.RootNode, 0));
            }

            List<JsonObject> objects = new List<JsonObject>();
            foreach (JsonNode? root in roots) Expand(root, objects);
            if (objects.Count == 0) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The manifest contains no objects.");
            if (objects.Count > MaxDocuments) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The manifest contains " + objects.Count + " objects; at most " + MaxDocuments + " are applied in one call.");
            foreach (JsonObject o in objects)
            {
                if (String.IsNullOrWhiteSpace(StringOf(o["kind"]))) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Every object in the manifest needs a kind.");
                if (String.IsNullOrWhiteSpace(StringOf(o["apiVersion"]))) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Every object in the manifest needs an apiVersion (" + StringOf(o["kind"]) + " has none).");
                if (String.IsNullOrWhiteSpace(StringOf((o["metadata"] as JsonObject)?["name"]))) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Every object in the manifest needs metadata.name (server-side apply does not support generateName).");
            }
            return objects;
        }

        /// <summary>
        /// Read a string property.
        /// </summary>
        /// <param name="node">Node.</param>
        /// <returns>String, or null when the node is not a string.</returns>
        public static string? StringOf(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string? s)) return s;
            return null;
        }

        /// <summary>
        /// Whether a node contains an explicit null anywhere (in server-side apply, null asks the server to remove a field).
        /// </summary>
        /// <param name="node">Node.</param>
        /// <returns>True when a property value is null.</returns>
        public static bool ContainsNull(JsonNode? node)
        {
            if (node is JsonObject obj) return obj.Any(kv => kv.Value == null || ContainsNull(kv.Value));
            if (node is JsonArray arr) return arr.Any(i => i == null || ContainsNull(i));
            return false;
        }

        #endregion

        #region Private-Methods

        private static void Expand(JsonNode? root, List<JsonObject> into)
        {
            if (root == null) return;
            if (root is JsonArray array)
            {
                foreach (JsonNode? item in array) Expand(item?.DeepClone(), into);
                return;
            }
            if (root is not JsonObject obj) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Each manifest document must be an object with apiVersion, kind, and metadata.");
            string? kind = StringOf(obj["kind"]);
            if (String.Equals(kind, "List", StringComparison.Ordinal) || (kind != null && kind.EndsWith("List", StringComparison.Ordinal) && obj["items"] is JsonArray))
            {
                if (obj["items"] is JsonArray items)
                {
                    foreach (JsonNode? item in items) Expand(item?.DeepClone(), into);
                }
                return;
            }
            into.Add(obj);
        }

        private static JsonNode? Convert(YamlNode node, int depth)
        {
            if (depth > 64) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The manifest is nested too deeply.");
            if (node is YamlMappingNode map)
            {
                JsonObject obj = new JsonObject();
                foreach (KeyValuePair<YamlNode, YamlNode> kv in map.Children)
                {
                    string key = kv.Key is YamlScalarNode ks ? (ks.Value ?? String.Empty) : kv.Key.ToString();
                    if (key == "<<") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "YAML merge keys (<<) are not supported in manifests.");
                    obj[key] = Convert(kv.Value, depth + 1);
                }
                return obj;
            }
            if (node is YamlSequenceNode seq)
            {
                JsonArray arr = new JsonArray();
                foreach (YamlNode child in seq.Children) arr.Add(Convert(child, depth + 1));
                return arr;
            }
            if (node is YamlScalarNode scalar) return Scalar(scalar);
            return null;
        }

        private static JsonNode? Scalar(YamlScalarNode scalar)
        {
            string value = scalar.Value ?? String.Empty;
            if (scalar.Style != ScalarStyle.Plain) return JsonValue.Create(value);
            if (!scalar.Tag.IsEmpty && !scalar.Tag.IsNonSpecific && scalar.Tag.Value.EndsWith(":str", StringComparison.Ordinal)) return JsonValue.Create(value);
            if (value.Length == 0 || value == "~" || value == "null" || value == "Null" || value == "NULL") return null;
            if (value == "true" || value == "True" || value == "TRUE") return JsonValue.Create(true);
            if (value == "false" || value == "False" || value == "FALSE") return JsonValue.Create(false);
            if (_Int.IsMatch(value) && Int64.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return JsonValue.Create(l);
            if (_Float.IsMatch(value) && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return JsonValue.Create(d);
            return JsonValue.Create(value);
        }

        #endregion
    }
}
