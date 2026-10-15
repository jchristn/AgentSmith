namespace Hugo.Connectors.Observability
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of <c>GET _cluster/health</c>.
    /// </summary>
    public class EsClusterHealth
    {
        /// <summary>
        /// Cluster name.
        /// </summary>
        [JsonPropertyName("cluster_name")]
        public string? ClusterName { get; set; } = null;

        /// <summary>
        /// Status: green, yellow, or red.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Whether the health request timed out.
        /// </summary>
        [JsonPropertyName("timed_out")]
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Nodes.
        /// </summary>
        [JsonPropertyName("number_of_nodes")]
        public int NumberOfNodes { get; set; } = 0;

        /// <summary>
        /// Data nodes.
        /// </summary>
        [JsonPropertyName("number_of_data_nodes")]
        public int NumberOfDataNodes { get; set; } = 0;

        /// <summary>
        /// Active primary shards.
        /// </summary>
        [JsonPropertyName("active_primary_shards")]
        public int ActivePrimaryShards { get; set; } = 0;

        /// <summary>
        /// Active shards.
        /// </summary>
        [JsonPropertyName("active_shards")]
        public int ActiveShards { get; set; } = 0;

        /// <summary>
        /// Relocating shards.
        /// </summary>
        [JsonPropertyName("relocating_shards")]
        public int RelocatingShards { get; set; } = 0;

        /// <summary>
        /// Initializing shards.
        /// </summary>
        [JsonPropertyName("initializing_shards")]
        public int InitializingShards { get; set; } = 0;

        /// <summary>
        /// Unassigned shards.
        /// </summary>
        [JsonPropertyName("unassigned_shards")]
        public int UnassignedShards { get; set; } = 0;

        /// <summary>
        /// Pending cluster tasks.
        /// </summary>
        [JsonPropertyName("number_of_pending_tasks")]
        public int NumberOfPendingTasks { get; set; } = 0;

        /// <summary>
        /// Percentage of shards active.
        /// </summary>
        [JsonPropertyName("active_shards_percent_as_number")]
        public double ActiveShardsPercent { get; set; } = 0;
    }
}
