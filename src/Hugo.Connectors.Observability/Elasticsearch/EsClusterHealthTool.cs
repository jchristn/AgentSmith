namespace Hugo.Connectors.Observability
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>es_cluster_health</c>: cluster status, nodes, and shard counts. Read. Thread-safe.
    /// </summary>
    public class EsClusterHealthTool : EsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema (no arguments).
        /// </summary>
        public static readonly string InputSchema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EsClusterHealthTool() : base(new ToolDescriptor
        {
            Name = "es_cluster_health",
            Title = "Elasticsearch cluster health",
            Description = "Report cluster status (green, yellow, red), node counts, and active, relocating, initializing, and unassigned shards. Check it first when searches are slow or failing.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Key facts as lines plus structured JSON of the health response."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            ElasticsearchSettings settings = ReadSettings(context);
            ObservabilityTransport transport = Transport(context.Resource, settings);
            string json = await transport.GetAsync("_cluster/health", null, token).ConfigureAwait(false);
            EsClusterHealth h = ObservabilityJson.DeserializeUpstream<EsClusterHealth>(json, transport.Provider);
            StringBuilder sb = new StringBuilder();
            sb.Append("cluster: ").Append(h.ClusterName).Append('\n');
            sb.Append("status: ").Append(h.Status).Append('\n');
            sb.Append("nodes: ").Append(h.NumberOfNodes.ToString(CultureInfo.InvariantCulture)).Append(" (data ").Append(h.NumberOfDataNodes.ToString(CultureInfo.InvariantCulture)).Append(")\n");
            sb.Append("shards: active ").Append(h.ActiveShards.ToString(CultureInfo.InvariantCulture))
                .Append(", primary ").Append(h.ActivePrimaryShards.ToString(CultureInfo.InvariantCulture))
                .Append(", relocating ").Append(h.RelocatingShards.ToString(CultureInfo.InvariantCulture))
                .Append(", initializing ").Append(h.InitializingShards.ToString(CultureInfo.InvariantCulture))
                .Append(", unassigned ").Append(h.UnassignedShards.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("active shards: ").Append(h.ActiveShardsPercent.ToString("0.#", CultureInfo.InvariantCulture)).Append("%\n");
            sb.Append("pending tasks: ").Append(h.NumberOfPendingTasks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (h.TimedOut) sb.Append("The health request timed out; the cluster may be overloaded.\n");
            ToolResult result = Finish(sb.ToString(), ObservabilityJson.Serialize(h), context.Limits, false);
            result.Metrics["unassignedShards"] = h.UnassignedShards;
            return result;
        }

        #endregion
    }
}
