namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// <c>k8s_top</c>: current CPU and memory use of pods or nodes from metrics-server (the <c>metrics.k8s.io</c> API).
    /// Always Read. Returns <c>not_supported</c> with a clear message when metrics-server is absent. Thread-safe.
    /// </summary>
    public class KubernetesTopTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""target"": { ""type"": ""string"", ""enum"": [""pods"", ""nodes""], ""description"": ""What to measure: pods (default) or nodes."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace for pods. Defaults to the resource's default namespace."" },
    ""allNamespaces"": { ""type"": ""boolean"", ""description"": ""Pods in every namespace this resource may access. Default false."" },
    ""name"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""One pod or node name."" },
    ""containers"": { ""type"": ""boolean"", ""description"": ""Show each container of each pod. Default false."" },
    ""sortBy"": { ""type"": ""string"", ""enum"": [""cpu"", ""memory"", ""name""], ""description"": ""Sort order. Default cpu (highest first)."" }
  },
  ""additionalProperties"": false
}";

        private const string _MetricsGroupVersion = "metrics.k8s.io/v1beta1";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesTopTool() : base(new ToolDescriptor
        {
            Name = "k8s_top",
            Title = "Show pod or node resource usage",
            Description = "Current CPU (millicores) and memory use of pods or nodes, like kubectl top, from metrics-server. "
                + "Use it for resource pressure, OOM risk, and noisy neighbours. If the cluster has no metrics-server the call fails with not_supported.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table of CPU and memory use, sorted by CPU by default."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            bool nodes = String.Equals(Optional(arguments, "target"), "nodes", StringComparison.OrdinalIgnoreCase);
            string? ns = nodes || arguments.GetBool("allNamespaces", false) ? null : Optional(arguments, "namespace");
            string summary = "Show " + (nodes ? "node" : "pod") + " resource usage" + (ns == null ? String.Empty : " in " + ns) + " on " + TargetName(context);
            return Scoped(options, nodes ? "Node" : null, null, ns, EffectEnum.Read, "reading metrics is read-only", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string resource = (Optional(arguments, "target") ?? "pods").ToLowerInvariant();
            if (resource != "pods" && resource != "nodes") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "target must be pods or nodes.");
            string? name = Optional(arguments, "name");
            string sortBy = (Optional(arguments, "sortBy") ?? "cpu").ToLowerInvariant();
            bool showContainers = arguments.GetBool("containers", false);

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            if (!await session.Discovery.ServesAsync(_MetricsGroupVersion, token).ConfigureAwait(false))
                throw new ConnectorException(ConnectorErrorCodes.NotSupported, "metrics-server is not installed on this cluster (the " + _MetricsGroupVersion + " API is not served), so live CPU and memory use is unavailable. Use k8s_describe to see requests and limits, or ask the operator to install metrics-server.");

            List<IList<string?>> rows = new List<IList<string?>>();
            List<string> columns;
            StringBuilder sb = new StringBuilder();
            if (resource == "nodes")
            {
                KubernetesNamespacePolicy.RequireClusterScoped(options, KubernetesKnownKinds.Find("Node") ?? new KubernetesResourceType { Kind = "Node" }, false);
                List<NodeMetrics> nodes = new List<NodeMetrics>();
                string basePath = "/apis/" + _MetricsGroupVersion + "/nodes";
                if (name != null) nodes.Add(KubernetesFormat.Typed<NodeMetrics>(await session.GetAsync(basePath + "/" + Uri.EscapeDataString(name), "node metrics for " + name, token).ConfigureAwait(false), "node metrics"));
                else nodes.AddRange(KubernetesFormat.Typed<NodeMetricsList>(await session.GetAsync(basePath, "node metrics", token).ConfigureAwait(false), "node metrics").Items ?? Enumerable.Empty<NodeMetrics>());
                columns = new List<string> { "NODE", "CPU(cores)", "MEMORY(bytes)", "WINDOW" };
                foreach (NodeMetrics n in Sort(nodes, m => Cpu(m.Usage), m => Memory(m.Usage), m => m.Metadata?.Name, sortBy).Take(context.Limits.MaxRows))
                    rows.Add(new List<string?> { n.Metadata?.Name, Millicores(Cpu(n.Usage)), Mebibytes(Memory(n.Usage)), n.Window });
                sb.Append(rows.Count).Append(" node(s)\n");
            }
            else
            {
                List<string?> scopes = new List<string?>();
                if (arguments.GetBool("allNamespaces", false))
                {
                    List<string>? allowed = KubernetesNamespacePolicy.AllNamespaces(options);
                    if (allowed == null) scopes.Add(null);
                    else scopes.AddRange(allowed);
                }
                else
                {
                    scopes.Add(KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace")));
                }

                List<PodMetrics> pods = new List<PodMetrics>();
                foreach (string? scope in scopes)
                {
                    string basePath = "/apis/" + _MetricsGroupVersion + (scope == null ? String.Empty : "/namespaces/" + Uri.EscapeDataString(scope)) + "/pods";
                    if (name != null && scope != null)
                    {
                        KubernetesApiResponse one = await session.SendAsync(System.Net.Http.HttpMethod.Get, basePath + "/" + Uri.EscapeDataString(name), null, null, null, token).ConfigureAwait(false);
                        if (one.IsSuccess) pods.Add(KubernetesFormat.Typed<PodMetrics>(one.Body, "pod metrics"));
                        else if (one.StatusCode != System.Net.HttpStatusCode.NotFound || scopes.Count == 1) throw KubernetesErrorMapper.FromResponse(one, "metrics for pod " + KubernetesFormat.Ref(scope, name));
                    }
                    else
                    {
                        PodMetricsList list = KubernetesFormat.Typed<PodMetricsList>(await session.GetAsync(basePath, "pod metrics", token).ConfigureAwait(false), "pod metrics");
                        pods.AddRange((list.Items ?? Enumerable.Empty<PodMetrics>()).Where(p => name == null || p.Metadata?.Name == name));
                    }
                }
                bool showNamespace = scopes.Count != 1 || scopes[0] == null;
                columns = new List<string>();
                if (showNamespace) columns.Add("NAMESPACE");
                columns.Add("POD");
                if (showContainers) columns.Add("CONTAINER");
                columns.Add("CPU(cores)");
                columns.Add("MEMORY(bytes)");
                foreach (PodMetrics p in Sort(pods, m => m.Containers?.Sum(c => Cpu(c.Usage)) ?? 0m, m => m.Containers?.Sum(c => Memory(c.Usage)) ?? 0m, m => m.Metadata?.Name, sortBy))
                {
                    if (rows.Count >= context.Limits.MaxRows) break;
                    if (showContainers)
                    {
                        foreach (ContainerMetrics c in p.Containers ?? new List<ContainerMetrics>())
                        {
                            List<string?> row = new List<string?>();
                            if (showNamespace) row.Add(p.Metadata?.NamespaceProperty);
                            row.Add(p.Metadata?.Name);
                            row.Add(c.Name);
                            row.Add(Millicores(Cpu(c.Usage)));
                            row.Add(Mebibytes(Memory(c.Usage)));
                            rows.Add(row);
                        }
                    }
                    else
                    {
                        List<string?> row = new List<string?>();
                        if (showNamespace) row.Add(p.Metadata?.NamespaceProperty);
                        row.Add(p.Metadata?.Name);
                        row.Add(Millicores(p.Containers?.Sum(c => Cpu(c.Usage)) ?? 0m));
                        row.Add(Mebibytes(p.Containers?.Sum(c => Memory(c.Usage)) ?? 0m));
                        rows.Add(row);
                    }
                }
                sb.Append(pods.Count).Append(" pod(s)\n");
            }

            if (rows.Count > 0) sb.Append(TextTable.Render(columns, rows));
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), OutputBudget(context), context.Limits.MaxLines, result);
            result.Metrics["rows"] = rows.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<T> Sort<T>(IEnumerable<T> items, Func<T, decimal> cpu, Func<T, decimal> memory, Func<T, string?> name, string sortBy)
        {
            if (sortBy == "memory") return items.OrderByDescending(memory);
            if (sortBy == "name") return items.OrderBy(i => name(i) ?? String.Empty, StringComparer.Ordinal);
            return items.OrderByDescending(cpu);
        }

        private static decimal Cpu(IDictionary<string, ResourceQuantity>? usage)
        {
            if (usage == null || !usage.TryGetValue("cpu", out ResourceQuantity? q) || q == null) return 0m;
            return q.ToDecimal();
        }

        private static decimal Memory(IDictionary<string, ResourceQuantity>? usage)
        {
            if (usage == null || !usage.TryGetValue("memory", out ResourceQuantity? q) || q == null) return 0m;
            return q.ToDecimal();
        }

        private static string Millicores(decimal cores)
        {
            return Math.Round(cores * 1000m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "m";
        }

        private static string Mebibytes(decimal bytes)
        {
            return Math.Round(bytes / (1024m * 1024m), 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "Mi";
        }

        #endregion
    }
}
