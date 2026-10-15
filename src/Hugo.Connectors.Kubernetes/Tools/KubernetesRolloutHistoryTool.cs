namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// <c>k8s_rollout_history</c>: the revisions of a Deployment (its ReplicaSets) or of a StatefulSet or DaemonSet
    /// (its ControllerRevisions), with change causes and images. Always Read. Thread-safe.
    /// </summary>
    public class KubernetesRolloutHistoryTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""kind"": { ""type"": ""string"", ""enum"": [""Deployment"", ""StatefulSet"", ""DaemonSet""], ""description"": ""Workload kind. Default Deployment."" },
    ""name"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Name of the Deployment, StatefulSet, or DaemonSet."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace. Defaults to the resource's default namespace."" }
  },
  ""required"": [""name""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesRolloutHistoryTool() : base(new ToolDescriptor
        {
            Name = "k8s_rollout_history",
            Title = "Show rollout history",
            Description = "List the revisions of a Deployment, StatefulSet, or DaemonSet, newest last, with the change-cause annotation, the container images, "
                + "and how many replicas each revision still runs. Use it to see what changed before a problem started.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table of revisions: revision, created, replicas, images, change cause."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            string kind = Optional(arguments, "kind") ?? "Deployment";
            string? ns = Optional(arguments, "namespace");
            string summary = "Show rollout history of " + kind + " " + KubernetesFormat.Ref(ns, Optional(arguments, "name") ?? "?") + " on " + TargetName(context);
            return Scoped(options, null, null, ns, EffectEnum.Read, "reading rollout history is read-only", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            KubernetesResourceType type = KubernetesRollout.Kind(Optional(arguments, "kind"));
            string name = Required(arguments, "name");
            string ns = KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace"));
            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;

            string ownerBody = await session.GetAsync(type.Path(ns, name), type.Kind.ToLowerInvariant() + " " + ns + "/" + name, token).ConfigureAwait(false);
            List<IList<string?>> rows = new List<IList<string?>>();
            List<string> columns = new List<string> { "REVISION", "CREATED", "REPLICAS", "IMAGES", "CHANGE-CAUSE" };

            if (type.Kind == "Deployment")
            {
                V1Deployment d = KubernetesFormat.Typed<V1Deployment>(ownerBody, "deployment");
                string selector = SelectorOf(d.Spec?.Selector);
                string path = "/apis/apps/v1/namespaces/" + Uri.EscapeDataString(ns) + "/replicasets" + KubernetesSession.Query("labelSelector", selector);
                V1ReplicaSetList list = KubernetesFormat.Typed<V1ReplicaSetList>(await session.GetAsync(path, "replicasets in " + ns, token).ConfigureAwait(false), "replicasets");
                IEnumerable<V1ReplicaSet> owned = (list.Items ?? new List<V1ReplicaSet>()).Where(rs => rs.Metadata?.OwnerReferences?.Any(o => o.Uid == d.Metadata?.Uid) == true);
                foreach (V1ReplicaSet rs in owned.OrderBy(rs => Revision(rs.Metadata)))
                {
                    rows.Add(new List<string?>
                    {
                        Revision(rs.Metadata).ToString(CultureInfo.InvariantCulture),
                        KubernetesFormat.Age(rs.Metadata?.CreationTimestamp, now),
                        (rs.Status?.Replicas ?? 0).ToString(CultureInfo.InvariantCulture),
                        String.Join(", ", (rs.Spec?.Template?.Spec?.Containers ?? new List<V1Container>()).Select(c => c.Image)),
                        Annotation(rs.Metadata, "kubernetes.io/change-cause") ?? "<none>"
                    });
                }
            }
            else
            {
                V1ObjectMeta? owner = type.Kind == "StatefulSet" ? KubernetesFormat.Typed<V1StatefulSet>(ownerBody, "statefulset").Metadata : KubernetesFormat.Typed<V1DaemonSet>(ownerBody, "daemonset").Metadata;
                string path = "/apis/apps/v1/namespaces/" + Uri.EscapeDataString(ns) + "/controllerrevisions";
                V1ControllerRevisionList list = KubernetesFormat.Typed<V1ControllerRevisionList>(await session.GetAsync(path, "controllerrevisions in " + ns, token).ConfigureAwait(false), "controllerrevisions");
                IEnumerable<V1ControllerRevision> owned = (list.Items ?? new List<V1ControllerRevision>()).Where(cr => cr.Metadata?.OwnerReferences?.Any(o => o.Uid == owner?.Uid) == true);
                foreach (V1ControllerRevision cr in owned.OrderBy(cr => cr.Revision ?? 0))
                {
                    rows.Add(new List<string?>
                    {
                        (cr.Revision ?? 0).ToString(CultureInfo.InvariantCulture),
                        KubernetesFormat.Age(cr.Metadata?.CreationTimestamp, now),
                        "-",
                        ImagesOf(cr.Data),
                        Annotation(cr.Metadata, "kubernetes.io/change-cause") ?? "<none>"
                    });
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(type.Kind.ToLowerInvariant()).Append(' ').Append(ns).Append('/').Append(name).Append(": ").Append(rows.Count).Append(" revision(s)\n");
            if (rows.Count > 0) sb.Append(TextTable.Render(columns, rows, 300));
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), OutputBudget(context), context.Limits.MaxLines, result);
            return result;
        }

        #endregion

        #region Private-Methods

        private static long Revision(V1ObjectMeta? meta)
        {
            string? value = Annotation(meta, "deployment.kubernetes.io/revision");
            return value != null && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long r) ? r : 0;
        }

        private static string? Annotation(V1ObjectMeta? meta, string key)
        {
            if (meta?.Annotations == null) return null;
            return meta.Annotations.TryGetValue(key, out string? value) ? value : null;
        }

        private static string SelectorOf(V1LabelSelector? selector)
        {
            if (selector?.MatchLabels == null || selector.MatchLabels.Count == 0) return String.Empty;
            return String.Join(",", selector.MatchLabels.Select(kv => kv.Key + "=" + kv.Value));
        }

        private static string ImagesOf(object? data)
        {
            if (data == null) return "-";
            JsonNode? node;
            try
            {
                node = data is JsonElement element ? JsonNode.Parse(element.GetRawText()) : JsonSerializer.SerializeToNode(data);
            }
            catch (JsonException)
            {
                return "-";
            }
            if (node?["spec"]?["template"]?["spec"]?["containers"] is JsonArray containers)
            {
                List<string> images = containers.OfType<JsonObject>().Select(c => KubernetesManifestParser.StringOf(c["image"]) ?? String.Empty).Where(i => i.Length > 0).ToList();
                if (images.Count > 0) return String.Join(", ", images);
            }
            return "-";
        }

        #endregion
    }
}
