namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Globalization;
    using System.Net.Http;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>k8s_rollout_restart</c>: restart a Deployment, StatefulSet, or DaemonSet by stamping its pod template, the way
    /// <c>kubectl rollout restart</c> does. Pods are replaced gradually under the workload's update strategy. Write.
    /// Thread-safe.
    /// </summary>
    public class KubernetesRolloutRestartTool : KubernetesToolBase
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
        public KubernetesRolloutRestartTool() : base(new ToolDescriptor
        {
            Name = "k8s_rollout_restart",
            Title = "Restart a workload",
            Description = "Restart every pod of a Deployment, StatefulSet, or DaemonSet with a rolling replacement (like kubectl rollout restart). "
                + "This is the safe way to restart: prefer it over deleting pods. Follow with k8s_rollout_status waitSeconds to confirm it finished. Write.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Write,
            OutputDescription = "Confirmation with the restartedAt timestamp."
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
            string summary = "Rolling restart of " + kind + " " + KubernetesFormat.Ref(ns ?? KubernetesNamespacePolicy.Effective(options, null), Optional(arguments, "name") ?? "?") + " on " + TargetName(context);
            return Scoped(options, null, null, ns, EffectEnum.Write, "a rolling restart replaces pods gradually", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            KubernetesResourceType type = KubernetesRollout.Kind(Optional(arguments, "kind"));
            string name = Required(arguments, "name");
            string ns = KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace"));
            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);

            string stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            JsonObject patch = new JsonObject
            {
                ["spec"] = new JsonObject
                {
                    ["template"] = new JsonObject
                    {
                        ["metadata"] = new JsonObject
                        {
                            ["annotations"] = new JsonObject { ["kubectl.kubernetes.io/restartedAt"] = stamp }
                        }
                    }
                }
            };
            string path = type.Path(ns, name) + KubernetesSession.Query("fieldManager", options.FieldManager);
            await session.SendCheckedAsync(HttpMethod.Patch, path, patch.ToJsonString(), "application/strategic-merge-patch+json", type.Kind.ToLowerInvariant() + " " + ns + "/" + name, token).ConfigureAwait(false);
            JsonObject structured = new JsonObject { ["kind"] = type.Kind, ["namespace"] = ns, ["name"] = name, ["restartedAt"] = stamp };
            return ToolResult.FromText(type.Kind.ToLowerInvariant() + " " + ns + "/" + name + " restarted (restartedAt " + stamp + "). Pods are being replaced; check progress with k8s_rollout_status.", structured.ToJsonString());
        }

        #endregion
    }
}
