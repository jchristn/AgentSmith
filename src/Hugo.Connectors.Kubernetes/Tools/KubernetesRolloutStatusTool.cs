namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// <c>k8s_rollout_status</c>: whether a Deployment, StatefulSet, or DaemonSet has finished rolling out, optionally
    /// waiting a bounded time. Always Read. Thread-safe.
    /// </summary>
    public class KubernetesRolloutStatusTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""kind"": { ""type"": ""string"", ""enum"": [""Deployment"", ""StatefulSet"", ""DaemonSet""], ""description"": ""Workload kind. Default Deployment."" },
    ""name"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Name of the Deployment, StatefulSet, or DaemonSet."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace. Defaults to the resource's default namespace."" },
    ""waitSeconds"": { ""type"": ""integer"", ""minimum"": 0, ""maximum"": 600, ""description"": ""Poll until the rollout completes or this many seconds pass (bounded by the call timeout). Default 0 checks once."" }
  },
  ""required"": [""name""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesRolloutStatusTool() : base(new ToolDescriptor
        {
            Name = "k8s_rollout_status",
            Title = "Check rollout status",
            Description = "Report whether a Deployment, StatefulSet, or DaemonSet has finished rolling out, using the rules of kubectl rollout status. "
                + "Pass waitSeconds to poll for completion after a change such as k8s_rollout_restart or k8s_apply.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A status line, plus structured JSON with done and failed flags."
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
            string summary = "Check rollout of " + kind + " " + KubernetesFormat.Ref(ns, Optional(arguments, "name") ?? "?") + " on " + TargetName(context);
            return Scoped(options, null, null, ns, EffectEnum.Read, "reading rollout status is read-only", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            KubernetesResourceType type = KubernetesRollout.Kind(Optional(arguments, "kind"));
            string name = Required(arguments, "name");
            string ns = KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace"));
            int waitSeconds = arguments.GetInt("waitSeconds", 0, 0, 600);

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            DateTime deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            KubernetesRolloutState state;
            while (true)
            {
                string body = await session.GetAsync(type.Path(ns, name), type.Kind.ToLowerInvariant() + " " + ns + "/" + name, token).ConfigureAwait(false);
                if (type.Kind == "Deployment") state = KubernetesRollout.Evaluate(KubernetesFormat.Typed<V1Deployment>(body, "deployment"));
                else if (type.Kind == "StatefulSet") state = KubernetesRollout.Evaluate(KubernetesFormat.Typed<V1StatefulSet>(body, "statefulset"));
                else state = KubernetesRollout.Evaluate(KubernetesFormat.Typed<V1DaemonSet>(body, "daemonset"));
                if (state.Done || state.Failed || DateTime.UtcNow >= deadline) break;
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            }

            JsonObject structured = new JsonObject { ["kind"] = type.Kind, ["namespace"] = ns, ["name"] = name, ["done"] = state.Done, ["failed"] = state.Failed, ["message"] = state.Message };
            ToolResult result = ToolResult.FromText(state.Message + (state.Done || state.Failed || waitSeconds == 0 ? String.Empty : " (still in progress after waiting " + waitSeconds + " seconds)"), structured.ToJsonString());
            if (state.Failed)
            {
                result.IsError = true;
                result.ErrorCode = "rollout_failed";
            }
            return result;
        }

        #endregion
    }
}
