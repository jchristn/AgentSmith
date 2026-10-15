namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// <c>k8s_describe</c>: a readable summary of one object plus its recent events, like <c>kubectl describe</c>.
    /// Always Read. Thread-safe.
    /// </summary>
    public class KubernetesDescribeTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""kind"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Kind or resource name, for example Pod, deploy, Node, or a custom resource kind."" },
    ""name"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Name of the object (metadata.name)."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace for namespaced kinds. Defaults to the resource's default namespace."" },
    ""apiVersion"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Optional apiVersion when a kind name exists in several API groups."" },
    ""eventLimit"": { ""type"": ""integer"", ""minimum"": 0, ""maximum"": 200, ""description"": ""Most related events to show, newest first. Default 20; 0 skips events."" }
  },
  ""required"": [""kind"", ""name""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesDescribeTool() : base(new ToolDescriptor
        {
            Name = "k8s_describe",
            Title = "Describe a Kubernetes object",
            Description = "Summarize one object the way kubectl describe does: status, containers and restarts, replicas, conditions, owners, and the recent events about it. "
                + "Start most investigations here, then read logs. Secret values are never shown, only key names and sizes.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Text summary followed by an events table."
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
            string summary = "Describe " + kind + " " + KubernetesFormat.Ref(ns, name) + " on " + TargetName(context);
            EffectClassification c = Scoped(options, kind, Optional(arguments, "apiVersion"), ns, EffectEnum.Read, "describing an object is read-only", summary);
            if (!c.Rejected && KubernetesKnownKinds.Find(kind)?.Kind == "Namespace" && !KubernetesNamespacePolicy.IsAllowed(options, name))
                return Rejected(KubernetesNamespacePolicy.Denied(options, name).Message, summary);
            return c;
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string kind = Required(arguments, "kind");
            string name = Required(arguments, "name");
            int eventLimit = arguments.GetInt("eventLimit", 20, 0, 200);

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            KubernetesTarget target = await ResolveTargetAsync(session, options, kind, Optional(arguments, "apiVersion"), Optional(arguments, "namespace"), false, token).ConfigureAwait(false);
            if (String.Equals(target.Type.Kind, "Namespace", StringComparison.Ordinal) && !KubernetesNamespacePolicy.IsAllowed(options, name)) throw KubernetesNamespacePolicy.Denied(options, name);

            string body = await session.GetAsync(target.Type.Path(target.Namespace, name), target.Label(name), token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            StringBuilder sb = new StringBuilder();
            sb.Append(KubernetesDescriber.Describe(target.Type, body, now));

            if (eventLimit > 0)
            {
                sb.Append("\nEvents:\n");
                string selector = "involvedObject.name=" + name + ",involvedObject.kind=" + target.Type.Kind;
                try
                {
                    List<Corev1Event> events;
                    if (target.Namespace != null)
                    {
                        events = await KubernetesEvents.ListAsync(session, target.Namespace, selector, 500, token).ConfigureAwait(false);
                    }
                    else
                    {
                        List<string>? allowed = KubernetesNamespacePolicy.AllNamespaces(options);
                        events = new List<Corev1Event>();
                        if (allowed == null) events.AddRange(await KubernetesEvents.ListAsync(session, null, selector, 500, token).ConfigureAwait(false));
                        else
                        {
                            foreach (string ns in allowed.Union(new[] { "default" }).Where(n => KubernetesNamespacePolicy.IsAllowed(options, n)))
                                events.AddRange(await KubernetesEvents.ListAsync(session, ns, selector, 500, token).ConfigureAwait(false));
                        }
                    }
                    events = events.Where(e => e.InvolvedObject == null || String.Equals(e.InvolvedObject.Name, name, StringComparison.Ordinal))
                        .OrderByDescending(e => KubernetesEvents.LastSeen(e) ?? DateTime.MinValue)
                        .Take(eventLimit)
                        .ToList();
                    if (events.Count == 0) sb.Append("  <none>\n");
                    else sb.Append(KubernetesEvents.Render(events, false, now));
                }
                catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.PermissionDenied)
                {
                    sb.Append("  Events are not readable with this credential (RBAC denies listing events).\n");
                }
            }

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), OutputBudget(context), context.Limits.MaxLines, result);
            return result;
        }

        #endregion
    }
}
