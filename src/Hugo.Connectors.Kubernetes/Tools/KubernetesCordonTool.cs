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
    /// <c>k8s_cordon</c>: mark a node unschedulable (cordon) or schedulable again (uncordon). It does not drain pods.
    /// Destructive, and Nodes are cluster-scoped, so the resource must allow cluster-scoped writes. Thread-safe.
    /// </summary>
    public class KubernetesCordonTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""node"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Node name. Find it with k8s_list kind Node."" },
    ""action"": { ""type"": ""string"", ""enum"": [""cordon"", ""uncordon""], ""description"": ""cordon stops new pods from being scheduled on the node; uncordon allows them again. Default cordon."" }
  },
  ""required"": [""node""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesCordonTool() : base(new ToolDescriptor
        {
            Name = "k8s_cordon",
            Title = "Cordon or uncordon a node",
            Description = "Mark a node unschedulable (cordon) or schedulable again (uncordon). Running pods stay where they are; this tool does not drain. "
                + "Destructive, needs approval, and works only when the resource allows cluster-scoped writes.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Destructive,
            OutputDescription = "The node's schedulable state before and after."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            string node = Optional(arguments, "node") ?? "?";
            string action = (Optional(arguments, "action") ?? "cordon").ToLowerInvariant();
            string summary = (action == "uncordon" ? "Uncordon" : "Cordon") + " node " + node + " on " + TargetName(context);
            return Scoped(options, "Node", null, null, EffectEnum.Destructive, action == "uncordon" ? "uncordoning changes where pods are scheduled" : "cordoning stops scheduling on a node", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string node = Required(arguments, "node");
            string action = (Optional(arguments, "action") ?? "cordon").ToLowerInvariant();
            if (action != "cordon" && action != "uncordon") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "action must be cordon or uncordon.");
            KubernetesResourceType nodeType = KubernetesKnownKinds.Find("Node") ?? new KubernetesResourceType { Kind = "Node", Plural = "nodes", Namespaced = false };
            KubernetesNamespacePolicy.RequireClusterScoped(options, nodeType, true);

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            string path = "/api/v1/nodes/" + Uri.EscapeDataString(node);
            V1Node before = KubernetesFormat.Typed<V1Node>(await session.GetAsync(path, "node " + node, token).ConfigureAwait(false), "node");
            bool wasUnschedulable = before.Spec?.Unschedulable == true;
            bool target = action == "cordon";
            if (wasUnschedulable != target)
            {
                JsonObject patch = new JsonObject { ["spec"] = new JsonObject { ["unschedulable"] = target } };
                await session.SendCheckedAsync(HttpMethod.Patch, path + KubernetesSession.Query("fieldManager", options.FieldManager), patch.ToJsonString(), "application/strategic-merge-patch+json", "node " + node, token).ConfigureAwait(false);
            }
            string text = "node/" + node + (wasUnschedulable == target ? " already " + (target ? "cordoned" : "uncordoned") : " " + (target ? "cordoned" : "uncordoned"))
                + ". Schedulable before: " + (!wasUnschedulable).ToString().ToLowerInvariant() + ", after: " + (!target).ToString().ToLowerInvariant() + "."
                + (target ? " Running pods were not evicted." : String.Empty);
            JsonObject structured = new JsonObject { ["node"] = node, ["action"] = action, ["unschedulableBefore"] = wasUnschedulable, ["unschedulable"] = target };
            return ToolResult.FromText(text, structured.ToJsonString());
        }

        #endregion
    }
}
