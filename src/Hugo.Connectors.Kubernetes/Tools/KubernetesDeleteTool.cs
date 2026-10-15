namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>k8s_delete</c>: delete one object of any kind. Destructive (Read for <c>dryRun</c>); deleting a Namespace,
    /// Node, CustomResourceDefinition, PersistentVolume, or StorageClass always requires approval. Thread-safe.
    /// </summary>
    public class KubernetesDeleteTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""kind"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Kind or resource name, for example Pod or a custom resource kind."" },
    ""name"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Object name. One object per call; there is no selector-based delete."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace for namespaced kinds. Defaults to the resource's default namespace."" },
    ""apiVersion"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Optional apiVersion when a kind name exists in several API groups."" },
    ""propagationPolicy"": { ""type"": ""string"", ""enum"": [""Background"", ""Foreground"", ""Orphan""], ""description"": ""What happens to dependents (for example a Deployment's ReplicaSets). Default Background."" },
    ""gracePeriodSeconds"": { ""type"": ""integer"", ""minimum"": 0, ""maximum"": 3600, ""description"": ""Seconds the object has to terminate gracefully. Default: the object's own setting."" },
    ""dryRun"": { ""type"": ""boolean"", ""description"": ""Ask the server whether the delete would succeed without deleting. Read. Default false."" }
  },
  ""required"": [""kind"", ""name""],
  ""additionalProperties"": false
}";

        private static readonly HashSet<string> _AlwaysApprove = new HashSet<string>(StringComparer.Ordinal)
        {
            "Namespace",
            "Node",
            "CustomResourceDefinition",
            "PersistentVolume",
            "StorageClass"
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesDeleteTool() : base(new ToolDescriptor
        {
            Name = "k8s_delete",
            Title = "Delete a Kubernetes object",
            Description = "Delete one object by kind and name. Destructive and usually needs human approval; deleting a Namespace, Node, CRD, PersistentVolume, or StorageClass always does. "
                + "To restart pods, prefer k8s_rollout_restart. Use dryRun: true to check the delete would be accepted.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Destructive,
            ClassifiedPerCall = true,
            OutputDescription = "Confirmation of the deletion (or of the dry run)."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            string kind = Optional(arguments, "kind") ?? "?";
            string name = Optional(arguments, "name") ?? "?";
            string? ns = Optional(arguments, "namespace");
            bool dryRun = arguments.GetBool("dryRun", false);
            KubernetesResourceType? known = KubernetesKnownKinds.Find(kind, Optional(arguments, "apiVersion"));
            string where = known != null && !known.Namespaced ? name : KubernetesFormat.Ref(ns ?? KubernetesNamespacePolicy.Effective(options, null), name);
            string summary = (dryRun ? "Dry-run delete of " : "Delete ") + (known?.Kind ?? kind) + " " + where + " on " + TargetName(context);
            EffectClassification c = Scoped(options, kind, Optional(arguments, "apiVersion"), ns, dryRun ? EffectEnum.Read : EffectEnum.Destructive, dryRun ? "server-side dry run deletes nothing" : "deleting an object is Destructive", summary);
            if (c.Rejected) return c;
            if (known?.Kind == "Namespace" && !KubernetesNamespacePolicy.IsAllowed(options, name)) return Rejected(KubernetesNamespacePolicy.Denied(options, name).Message, summary);
            if (!dryRun && known != null && _AlwaysApprove.Contains(known.Kind))
            {
                c.AlwaysRequireApproval = true;
                c.Reason = "deleting a " + known.Kind + " can destroy data or take down workloads, so it always needs approval";
            }
            return c;
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string kind = Required(arguments, "kind");
            string name = Required(arguments, "name");
            bool dryRun = arguments.GetBool("dryRun", false);
            string propagation = Optional(arguments, "propagationPolicy") ?? "Background";
            if (propagation != "Background" && propagation != "Foreground" && propagation != "Orphan") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "propagationPolicy must be Background, Foreground, or Orphan.");
            long? grace = arguments.Has("gracePeriodSeconds") ? arguments.GetLong("gracePeriodSeconds", 0, 0, 3600) : null;

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            KubernetesTarget target = await ResolveTargetAsync(session, options, kind, Optional(arguments, "apiVersion"), Optional(arguments, "namespace"), !dryRun, token).ConfigureAwait(false);
            if (String.Equals(target.Type.Kind, "Namespace", StringComparison.Ordinal) && !KubernetesNamespacePolicy.IsAllowed(options, name)) throw KubernetesNamespacePolicy.Denied(options, name);
            if (!target.Type.Supports("delete")) throw new ConnectorException(ConnectorErrorCodes.NotSupported, target.Type.DisplayName + " cannot be deleted.");

            JsonObject body = new JsonObject { ["apiVersion"] = "v1", ["kind"] = "DeleteOptions", ["propagationPolicy"] = propagation };
            if (grace.HasValue) body["gracePeriodSeconds"] = grace.Value;
            if (dryRun) body["dryRun"] = new JsonArray("All");
            await session.SendCheckedAsync(HttpMethod.Delete, target.Type.Path(target.Namespace, name), body.ToJsonString(), "application/json", target.Label(name), token).ConfigureAwait(false);

            string text = target.Label(name) + (dryRun ? " would be deleted (server dry run; nothing changed)." : " deleted (propagation " + propagation + "). Dependents and finalizers may take a moment to finish.");
            JsonObject structured = new JsonObject { ["kind"] = target.Type.Kind, ["namespace"] = target.Namespace, ["name"] = name, ["dryRun"] = dryRun, ["propagationPolicy"] = propagation };
            return ToolResult.FromText(text, structured.ToJsonString());
        }

        #endregion
    }
}
