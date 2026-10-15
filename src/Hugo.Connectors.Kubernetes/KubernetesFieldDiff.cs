namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Finds fields an apply would remove by comparing the live object with the server's dry-run result. Status and
    /// server-managed metadata are ignored. Lists are compared by index, so removing a list element reports its
    /// fields. Thread-safe.
    /// </summary>
    public static class KubernetesFieldDiff
    {
        #region Private-Members

        private static readonly HashSet<string> _Ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "status",
            "metadata.managedFields",
            "metadata.resourceVersion",
            "metadata.generation",
            "metadata.uid",
            "metadata.creationTimestamp",
            "metadata.selfLink",
            "metadata.annotations.deployment.kubernetes.io/revision",
            "metadata.annotations.kubectl.kubernetes.io/last-applied-configuration"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Leaf paths present in <paramref name="live"/> but absent from <paramref name="after"/>.
        /// </summary>
        /// <param name="live">Live object.</param>
        /// <param name="after">Dry-run result.</param>
        /// <param name="max">Most paths returned. Default 50.</param>
        /// <returns>Paths such as <c>spec.template.spec.containers[0].ports</c>.</returns>
        public static List<string> RemovedPaths(JsonNode? live, JsonNode? after, int max = 50)
        {
            List<string> removed = new List<string>();
            Walk(live, after, String.Empty, removed, Math.Max(1, max));
            return removed;
        }

        /// <summary>
        /// Whether two versions of an object have the same content, ignoring status and server-managed metadata.
        /// </summary>
        /// <param name="before">First version.</param>
        /// <param name="after">Second version.</param>
        /// <returns>True when no field was added, removed, or changed.</returns>
        public static bool SameContent(JsonNode? before, JsonNode? after)
        {
            return JsonNode.DeepEquals(Strip(before), Strip(after));
        }

        #endregion

        #region Private-Methods

        private static JsonNode? Strip(JsonNode? node)
        {
            if (node is not JsonObject obj) return node?.DeepClone();
            JsonObject copy = (JsonObject)obj.DeepClone();
            copy.Remove("status");
            if (copy["metadata"] is JsonObject metadata)
            {
                foreach (string key in new[] { "managedFields", "resourceVersion", "generation", "uid", "creationTimestamp", "selfLink" }) metadata.Remove(key);
                if (metadata["annotations"] is JsonObject annotations)
                {
                    annotations.Remove("deployment.kubernetes.io/revision");
                    if (annotations.Count == 0) metadata.Remove("annotations");
                }
            }
            return copy;
        }

        private static void Walk(JsonNode? live, JsonNode? after, string path, List<string> removed, int max)
        {
            if (removed.Count >= max) return;
            if (_Ignored.Contains(path)) return;
            if (live is JsonObject lo)
            {
                JsonObject? ao = after as JsonObject;
                foreach (KeyValuePair<string, JsonNode?> kv in lo)
                {
                    string child = path.Length == 0 ? kv.Key : path + "." + kv.Key;
                    if (_Ignored.Contains(child)) continue;
                    if (ao == null || !ao.ContainsKey(kv.Key))
                    {
                        if (kv.Value != null) removed.Add(child);
                        if (removed.Count >= max) return;
                        continue;
                    }
                    Walk(kv.Value, ao[kv.Key], child, removed, max);
                }
                return;
            }
            if (live is JsonArray la)
            {
                JsonArray? aa = after as JsonArray;
                for (int i = 0; i < la.Count; i++)
                {
                    string child = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    if (aa == null || i >= aa.Count)
                    {
                        removed.Add(child);
                        if (removed.Count >= max) return;
                        continue;
                    }
                    Walk(la[i], aa[i], child, removed, max);
                }
            }
        }

        #endregion
    }
}
