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
    /// <c>k8s_events</c>: recent events in a namespace (or every allowed namespace), filtered by type, object, and age.
    /// Always Read. Thread-safe.
    /// </summary>
    public class KubernetesEventsTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace. Defaults to the resource's default namespace."" },
    ""allNamespaces"": { ""type"": ""boolean"", ""description"": ""Read events from every namespace this resource may access. Default false."" },
    ""type"": { ""type"": ""string"", ""enum"": [""Normal"", ""Warning""], ""description"": ""Only events of this type. Warning is the usual filter when something is broken."" },
    ""kind"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Only events about objects of this kind, for example Pod."" },
    ""name"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Only events about the object with this name."" },
    ""sinceMinutes"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 10080, ""description"": ""Only events last seen in this many minutes. Default 60."" },
    ""limit"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 1000, ""description"": ""Most events to return, newest first. Default 50."" }
  },
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesEventsTool() : base(new ToolDescriptor
        {
            Name = "k8s_events",
            Title = "List Kubernetes events",
            Description = "Recent cluster events, newest first: scheduling failures, image pull errors, OOM kills, probe failures, scaling. "
                + "Filter by type (Warning), by the object's kind and name, and by age in minutes. Events expire after about an hour in most clusters.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table of events: last seen, type, reason, object, count, message."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            string? ns = arguments.GetBool("allNamespaces", false) ? null : Optional(arguments, "namespace");
            string summary = "List events" + (ns == null ? String.Empty : " in " + ns) + " on " + TargetName(context);
            return Scoped(options, null, null, ns, EffectEnum.Read, "reading events is read-only", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            bool allNamespaces = arguments.GetBool("allNamespaces", false);
            string? type = Optional(arguments, "type");
            string? kind = Optional(arguments, "kind");
            string? name = Optional(arguments, "name");
            int sinceMinutes = arguments.GetInt("sinceMinutes", 60, 1, 10080);
            int limit = Math.Min(arguments.GetInt("limit", 50, 1, 1000), context.Limits.MaxRows);

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            if (kind != null)
            {
                KubernetesResourceType? resolved = KubernetesKnownKinds.Find(kind);
                if (resolved != null) kind = resolved.Kind;
            }

            List<string> selectors = new List<string>();
            if (type != null) selectors.Add("type=" + type);
            if (kind != null) selectors.Add("involvedObject.kind=" + kind);
            if (name != null) selectors.Add("involvedObject.name=" + name);
            string? fieldSelector = selectors.Count == 0 ? null : String.Join(",", selectors);

            List<string?> scopes = new List<string?>();
            if (allNamespaces)
            {
                List<string>? allowed = KubernetesNamespacePolicy.AllNamespaces(options);
                if (allowed == null) scopes.Add(null);
                else scopes.AddRange(allowed);
            }
            else
            {
                scopes.Add(KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace")));
            }

            DateTime now = DateTime.UtcNow;
            DateTime cutoff = now.AddMinutes(-sinceMinutes);
            List<Corev1Event> events = new List<Corev1Event>();
            foreach (string? scope in scopes)
            {
                token.ThrowIfCancellationRequested();
                events.AddRange(await KubernetesEvents.ListAsync(session, scope, fieldSelector, 2000, token).ConfigureAwait(false));
            }
            List<Corev1Event> selected = events
                .Where(e => (KubernetesEvents.LastSeen(e) ?? now) >= cutoff)
                .OrderByDescending(e => KubernetesEvents.LastSeen(e) ?? DateTime.MinValue)
                .Take(limit)
                .ToList();

            StringBuilder sb = new StringBuilder();
            string where = scopes.Count == 1 && scopes[0] != null ? "namespace " + scopes[0] : (scopes.Count > 1 ? "namespaces " + String.Join(", ", scopes) : "all namespaces");
            sb.Append(selected.Count).Append(" event(s) in ").Append(where).Append(" from the last ").Append(sinceMinutes).Append(" minute(s)");
            if (fieldSelector != null) sb.Append(" matching ").Append(fieldSelector);
            sb.Append('\n');
            if (selected.Count > 0) sb.Append(KubernetesEvents.Render(selected, scopes.Count != 1 || scopes[0] == null, now));

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), OutputBudget(context), context.Limits.MaxLines, result);
            result.Metrics["rows"] = selected.Count;
            return result;
        }

        #endregion
    }
}
