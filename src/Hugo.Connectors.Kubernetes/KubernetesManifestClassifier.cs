namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classifies a <c>k8s_apply</c> call without contacting the cluster. Rules, most severe first:
    /// a manifest that names a namespace outside the allow list, or a known cluster-scoped kind while
    /// <c>allowClusterScoped</c> is false, is Rejected; an unparseable manifest is Destructive (fail closed);
    /// <c>dryRun</c> is Read; a kind in <c>destructiveKinds</c>, an explicit null (field removal), <c>force</c>
    /// (taking over fields another manager owns), or <c>allowFieldRemoval</c> is Destructive; anything else is Write.
    /// At execution, an apply that would remove fields without <c>allowFieldRemoval</c> is refused. Thread-safe.
    /// </summary>
    public static class KubernetesManifestClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify an apply.
        /// </summary>
        /// <param name="manifest">Manifest text.</param>
        /// <param name="options">Resource options.</param>
        /// <param name="namespaceArgument">The call's namespace argument, or null.</param>
        /// <param name="dryRun">Whether the call is a server-side dry run.</param>
        /// <param name="force">Whether conflicts are forced.</param>
        /// <param name="allowFieldRemoval">Whether the call may remove fields.</param>
        /// <param name="target">Target name for the summary (binding alias or resource name).</param>
        /// <returns>Classification.</returns>
        public static EffectClassification Classify(string? manifest, KubernetesOptions options, string? namespaceArgument, bool dryRun, bool force, bool allowFieldRemoval, string target)
        {
            ArgumentNullException.ThrowIfNull(options);
            List<JsonObject> objects;
            try
            {
                objects = KubernetesManifestParser.Parse(manifest);
            }
            catch (ConnectorException e)
            {
                return new EffectClassification(EffectEnum.Destructive, "the manifest could not be parsed (" + e.Message + "), so it is treated as Destructive", "Apply an unparseable manifest on " + target);
            }

            List<string> labels = new List<string>();
            List<string> destructiveReasons = new List<string>();
            foreach (JsonObject obj in objects)
            {
                string kind = KubernetesManifestParser.StringOf(obj["kind"]) ?? "?";
                string apiVersion = KubernetesManifestParser.StringOf(obj["apiVersion"]) ?? String.Empty;
                JsonObject? metadata = obj["metadata"] as JsonObject;
                string name = KubernetesManifestParser.StringOf(metadata?["name"]) ?? "?";
                string? docNamespace = KubernetesManifestParser.StringOf(metadata?["namespace"]);
                bool clusterScoped = KubernetesKnownKinds.IsClusterScoped(kind, apiVersion);

                if (clusterScoped)
                {
                    labels.Add(kind + " " + name);
                    string? reason = KubernetesNamespacePolicy.ExplainClusterScoped(options, kind, !dryRun);
                    if (reason != null) return Rejected(reason, "Apply " + kind + " " + name + " on " + target);
                    if (String.Equals(KubernetesKnownKinds.Find(kind, apiVersion)?.Kind, "Namespace", StringComparison.Ordinal) && !KubernetesNamespacePolicy.IsAllowed(options, name))
                        return Rejected(KubernetesNamespacePolicy.Denied(options, name).Message, "Apply Namespace " + name + " on " + target);
                }
                else
                {
                    string ns = KubernetesNamespacePolicy.Effective(options, String.IsNullOrWhiteSpace(docNamespace) ? namespaceArgument : docNamespace);
                    labels.Add(kind + " " + ns + "/" + name);
                    if (!String.IsNullOrWhiteSpace(docNamespace) && !String.IsNullOrWhiteSpace(namespaceArgument) && !String.Equals(docNamespace, namespaceArgument.Trim(), StringComparison.Ordinal))
                        return Rejected("the manifest sets namespace '" + docNamespace + "' but the call asked for namespace '" + namespaceArgument.Trim() + "'", "Apply " + kind + " " + name + " on " + target);
                    string? nsReason = KubernetesNamespacePolicy.ExplainNamespace(options, ns);
                    if (nsReason != null) return Rejected(nsReason, "Apply " + kind + " " + ns + "/" + name + " on " + target);
                }

                if (options.IsDestructiveKind(kind)) destructiveReasons.Add("kind " + kind + " is in destructiveKinds");
                if (KubernetesManifestParser.ContainsNull(obj)) destructiveReasons.Add(kind + " " + name + " sets a field to null, which removes it");
            }

            string summary = (dryRun ? "Dry-run apply of " : "Apply ") + Describe(labels) + " on " + target + (force ? " (force conflicts)" : String.Empty);
            if (dryRun) return new EffectClassification(EffectEnum.Read, "server-side dry run changes nothing", summary);
            if (force) destructiveReasons.Add("force takes over fields owned by other managers");
            if (allowFieldRemoval) destructiveReasons.Add("allowFieldRemoval lets the apply remove fields");
            if (destructiveReasons.Count > 0) return new EffectClassification(EffectEnum.Destructive, String.Join("; ", destructiveReasons.Distinct()), summary);
            return new EffectClassification(EffectEnum.Write, "server-side apply creates or updates " + objects.Count + " object(s)", summary);
        }

        #endregion

        #region Private-Methods

        private static EffectClassification Rejected(string reason, string summary)
        {
            EffectClassification c = EffectClassification.Reject(reason);
            c.ApprovalSummary = summary;
            return c;
        }

        private static string Describe(List<string> labels)
        {
            if (labels.Count <= 3) return String.Join(", ", labels);
            return String.Join(", ", labels.Take(3)) + " and " + (labels.Count - 3) + " more";
        }

        #endregion
    }
}
