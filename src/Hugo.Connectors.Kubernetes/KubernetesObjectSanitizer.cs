namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Cleans Kubernetes objects before they are shown to a model: removes <c>metadata.managedFields</c> and ALWAYS
    /// redacts Secret values (<c>data</c>, <c>stringData</c>, and the <c>last-applied-configuration</c> annotation, which
    /// repeats them). Works on single objects, lists, and objects nested anywhere in a document. Generic objects of any
    /// kind are handled as JSON nodes because their shape is not a fixed contract. Thread-safe.
    /// </summary>
    public static class KubernetesObjectSanitizer
    {
        #region Public-Members

        /// <summary>
        /// Annotation that kubectl client-side apply writes with the full previous manifest.
        /// </summary>
        public static string LastAppliedAnnotation { get; } = "kubectl.kubernetes.io/last-applied-configuration";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sanitize a node in place.
        /// </summary>
        /// <param name="node">Object or list, or null.</param>
        /// <param name="kindHint">Kind of the object, or of the list items when the API omits it (list responses do), or null.</param>
        /// <param name="stripManagedFields">Whether to remove <c>metadata.managedFields</c>. Default true.</param>
        /// <returns>The same node.</returns>
        public static JsonNode? Sanitize(JsonNode? node, string? kindHint, bool stripManagedFields = true)
        {
            if (node is JsonObject obj)
            {
                string? kind = StringOf(obj["kind"]);
                if (obj["items"] is JsonArray items)
                {
                    string? itemKind = kindHint;
                    if (!String.IsNullOrEmpty(kind) && kind.EndsWith("List", StringComparison.Ordinal) && kind.Length > 4) itemKind = kind.Substring(0, kind.Length - 4);
                    foreach (JsonNode? item in items) Sanitize(item, itemKind, stripManagedFields);
                }
                SanitizeObject(obj, String.IsNullOrEmpty(kind) ? kindHint : kind, stripManagedFields);
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array) Sanitize(item, kindHint, stripManagedFields);
            }
            return node;
        }

        /// <summary>
        /// Whether a sanitized object still holds secret values (used by tests and as a final guard).
        /// </summary>
        /// <param name="node">Node.</param>
        /// <returns>True when a Secret's data or stringData holds a value that is not a redaction marker.</returns>
        public static bool HasSecretValues(JsonNode? node)
        {
            if (node is JsonArray array) return array.Any(HasSecretValues);
            if (node is not JsonObject obj) return false;
            if (String.Equals(StringOf(obj["kind"]), "Secret", StringComparison.Ordinal))
            {
                foreach (string field in new[] { "data", "stringData" })
                {
                    if (obj[field] is JsonObject values && values.Any(kv => kv.Value != null && !(StringOf(kv.Value) ?? String.Empty).StartsWith("<redacted", StringComparison.Ordinal))) return true;
                }
            }
            return obj.Any(kv => HasSecretValues(kv.Value));
        }

        /// <summary>
        /// The redaction marker for a base64 <c>data</c> value.
        /// </summary>
        /// <param name="base64">Base64 value, or null.</param>
        /// <returns>Marker such as <c>&lt;redacted: 12 bytes&gt;</c>.</returns>
        public static string Marker(string? base64)
        {
            int bytes = 0;
            if (!String.IsNullOrEmpty(base64))
            {
                try
                {
                    bytes = Convert.FromBase64String(base64).Length;
                }
                catch (FormatException)
                {
                    bytes = Encoding.UTF8.GetByteCount(base64);
                }
            }
            return "<redacted: " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes>";
        }

        #endregion

        #region Private-Methods

        private static void SanitizeObject(JsonObject obj, string? kind, bool stripManagedFields)
        {
            if (obj["metadata"] is JsonObject metadata)
            {
                if (stripManagedFields) metadata.Remove("managedFields");
                if (String.Equals(kind, "Secret", StringComparison.Ordinal) && metadata["annotations"] is JsonObject annotations && annotations.ContainsKey(LastAppliedAnnotation))
                    annotations[LastAppliedAnnotation] = "<redacted: contains secret values>";
            }

            if (String.Equals(kind, "Secret", StringComparison.Ordinal))
            {
                if (obj["data"] is JsonObject data)
                {
                    foreach (string key in data.Select(kv => kv.Key).ToList()) data[key] = Marker(StringOf(data[key]));
                }
                if (obj["stringData"] is JsonObject stringData)
                {
                    foreach (string key in stringData.Select(kv => kv.Key).ToList())
                        stringData[key] = "<redacted: " + Encoding.UTF8.GetByteCount(StringOf(stringData[key]) ?? String.Empty).ToString(CultureInfo.InvariantCulture) + " bytes>";
                }
            }

            List<string> keys = obj.Select(kv => kv.Key).Where(k => k != "metadata" && k != "items").ToList();
            foreach (string key in keys)
            {
                JsonNode? child = obj[key];
                if (child is JsonObject || child is JsonArray) Sanitize(child, null, stripManagedFields);
            }
        }

        private static string? StringOf(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string? s)) return s;
            return null;
        }

        #endregion
    }
}
