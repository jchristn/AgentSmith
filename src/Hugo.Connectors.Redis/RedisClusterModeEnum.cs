namespace Hugo.Connectors.Redis
{
    /// <summary>
    /// How the connector finds the Redis primary or primaries.
    /// </summary>
    public enum RedisClusterModeEnum
    {
        /// <summary>Detect standalone or cluster from the server.</summary>
        Auto = 0,

        /// <summary>A single primary, optionally with replicas.</summary>
        Standalone = 1,

        /// <summary>Redis Cluster; key commands are routed by hash slot and scans visit every primary.</summary>
        Cluster = 2,

        /// <summary>The endpoints are Sentinels; the primary of <c>sentinelServiceName</c> is resolved through them.</summary>
        Sentinel = 3
    }
}
