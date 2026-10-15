namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>k8s_get</c>: one object as YAML (or JSON), with <c>managedFields</c> removed and Secret values ALWAYS
    /// redacted. Always Read. Thread-safe.
    /// </summary>
    public class KubernetesGetTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""kind"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Kind or resource name, for example Deployment, svc, or a custom resource kind."" },
    ""name"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Name of the object (metadata.name)."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace for namespaced kinds. Defaults to the resource's default namespace."" },
    ""apiVersion"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Optional apiVersion when a kind name exists in several API groups."" },
    ""output"": { ""type"": ""string"", ""enum"": [""yaml"", ""json""], ""description"": ""Output format. Default yaml."" }
  },
  ""required"": [""kind"", ""name""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesGetTool() : base(new ToolDescriptor
        {
            Name = "k8s_get",
            Title = "Get a Kubernetes object",
            Description = "Return one object of any kind as YAML (default) or JSON, without managedFields. Use it to read a full spec or status. "
                + "Secret values are always redacted (key names and sizes remain). Prefer k8s_describe for a readable summary with events.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "The object as YAML or JSON text."
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
            string summary = "Get " + kind + " " + KubernetesFormat.Ref(ns, name) + " on " + TargetName(context);
            EffectClassification c = Scoped(options, kind, Optional(arguments, "apiVersion"), ns, EffectEnum.Read, "reading an object is read-only", summary);
            if (!c.Rejected && KubernetesKnownKinds.Find(kind)?.Kind == "Namespace" && !KubernetesNamespacePolicy.IsAllowed(options, name))
                return Rejected(KubernetesNamespacePolicy.Denied(options, name).Message, summary);
            return c;
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string kind = Required(arguments, "kind");
            string name = Required(arguments, "name");
            string output = (Optional(arguments, "output") ?? "yaml").ToLowerInvariant();
            if (output != "yaml" && output != "json") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "output must be yaml or json.");

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            KubernetesTarget target = await ResolveTargetAsync(session, options, kind, Optional(arguments, "apiVersion"), Optional(arguments, "namespace"), false, token).ConfigureAwait(false);
            if (String.Equals(target.Type.Kind, "Namespace", StringComparison.Ordinal) && !KubernetesNamespacePolicy.IsAllowed(options, name)) throw KubernetesNamespacePolicy.Denied(options, name);

            string body = await session.GetAsync(target.Type.Path(target.Namespace, name), target.Label(name), token).ConfigureAwait(false);
            JsonNode node = KubernetesFormat.ParseNode(body, target.Label(name));
            KubernetesObjectSanitizer.Sanitize(node, target.Type.Kind);

            string text = output == "json" ? node.ToJsonString(KubernetesFormat.Indented) : KubernetesYamlWriter.Write(node);
            ToolResult result = new ToolResult();
            result.MimeType = output == "json" ? "application/json" : "application/yaml";
            result.Text = OutputLimiter.Head(text, OutputBudget(context), context.Limits.MaxLines, result);
            return result;
        }

        #endregion
    }
}
