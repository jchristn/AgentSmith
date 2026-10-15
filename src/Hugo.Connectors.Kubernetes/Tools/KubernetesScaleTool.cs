namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Net.Http;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// <c>k8s_scale</c>: set the replica count of a Deployment, StatefulSet, ReplicaSet, or any kind with a scale
    /// subresource. Write; scaling to zero replicas is Destructive because it stops the workload. Thread-safe.
    /// </summary>
    public class KubernetesScaleTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""kind"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Kind with a scale subresource: Deployment (default), StatefulSet, ReplicaSet, or a custom resource that supports scale."" },
    ""name"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Name of the Deployment, StatefulSet, or DaemonSet."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace. Defaults to the resource's default namespace."" },
    ""apiVersion"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Optional apiVersion when a kind name exists in several API groups."" },
    ""replicas"": { ""type"": ""integer"", ""minimum"": 0, ""maximum"": 1000, ""description"": ""Desired replica count. 0 stops the workload and is Destructive."" }
  },
  ""required"": [""name"", ""replicas""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesScaleTool() : base(new ToolDescriptor
        {
            Name = "k8s_scale",
            Title = "Scale a workload",
            Description = "Set the replica count of a Deployment, StatefulSet, ReplicaSet, or a custom resource with a scale subresource. Write; scaling to 0 is Destructive. "
                + "A HorizontalPodAutoscaler may override the count. State the current and new counts before calling.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Write,
            ClassifiedPerCall = true,
            OutputDescription = "The previous and new replica counts."
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
            long replicas = arguments.GetLong("replicas", -1);
            string summary = "Scale " + kind + " " + KubernetesFormat.Ref(ns ?? KubernetesNamespacePolicy.Effective(options, null), Optional(arguments, "name") ?? "?") + " to " + replicas + " replica(s) on " + TargetName(context);
            if (replicas < 0) return new EffectClassification(EffectEnum.Destructive, "the replica count is missing or invalid, so the call is treated as Destructive", summary);
            if (replicas == 0) return Scoped(options, kind, Optional(arguments, "apiVersion"), ns, EffectEnum.Destructive, "scaling to 0 replicas stops the workload", summary);
            return Scoped(options, kind, Optional(arguments, "apiVersion"), ns, EffectEnum.Write, "scaling changes the replica count", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string kind = Optional(arguments, "kind") ?? "Deployment";
            string name = Required(arguments, "name");
            if (!arguments.Has("replicas")) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'replicas' is required.");
            int replicas = arguments.GetInt("replicas", 0, 0, 1000);

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            KubernetesTarget target = await ResolveTargetAsync(session, options, kind, Optional(arguments, "apiVersion"), Optional(arguments, "namespace"), true, token).ConfigureAwait(false);
            if (!target.Type.Subresources.Contains("scale")) throw new ConnectorException(ConnectorErrorCodes.NotSupported, target.Type.DisplayName + " has no scale subresource, so it cannot be scaled.");

            string path = target.Type.Path(target.Namespace, name, "scale");
            V1Scale before = KubernetesFormat.Typed<V1Scale>(await session.GetAsync(path, target.Label(name), token).ConfigureAwait(false), "scale");
            JsonObject patch = new JsonObject { ["spec"] = new JsonObject { ["replicas"] = replicas } };
            V1Scale after = KubernetesFormat.Typed<V1Scale>(await session.SendCheckedAsync(HttpMethod.Patch, path + KubernetesSession.Query("fieldManager", options.FieldManager), patch.ToJsonString(), "application/merge-patch+json", target.Label(name), token).ConfigureAwait(false), "scale");

            int previous = before.Spec?.Replicas ?? 0;
            int now = after.Spec?.Replicas ?? replicas;
            JsonObject structured = new JsonObject { ["kind"] = target.Type.Kind, ["namespace"] = target.Namespace, ["name"] = name, ["previousReplicas"] = previous, ["replicas"] = now, ["currentReplicas"] = after.Status?.Replicas ?? 0 };
            string text = target.Label(name) + " scaled from " + previous + " to " + now + " replica(s)" + (previous == now ? " (unchanged)" : String.Empty) + ". Check progress with k8s_rollout_status or k8s_list.";
            return ToolResult.FromText(text, structured.ToJsonString());
        }

        #endregion
    }
}
