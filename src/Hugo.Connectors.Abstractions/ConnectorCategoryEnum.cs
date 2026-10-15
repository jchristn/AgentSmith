namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Broad category of a connector, used for grouping in catalogs and the dashboard.
    /// </summary>
    public enum ConnectorCategoryEnum
    {
        /// <summary>
        /// Hosts and shells (SSH).
        /// </summary>
        Compute = 0,

        /// <summary>
        /// Object and file storage.
        /// </summary>
        Storage = 1,

        /// <summary>
        /// Relational and document databases.
        /// </summary>
        Database = 2,

        /// <summary>
        /// Log search services.
        /// </summary>
        Logging = 3,

        /// <summary>
        /// Caches and key-value stores.
        /// </summary>
        Cache = 4,

        /// <summary>
        /// Cloud provider control planes.
        /// </summary>
        Cloud = 5,

        /// <summary>
        /// Container orchestration and container hosts.
        /// </summary>
        Orchestration = 6,

        /// <summary>
        /// Metrics, traces, and search backends.
        /// </summary>
        Observability = 7,

        /// <summary>
        /// Generic HTTP APIs.
        /// </summary>
        Http = 8,

        /// <summary>
        /// Other agents and MCP servers.
        /// </summary>
        Agent = 9,

        /// <summary>
        /// Anything else, including test connectors.
        /// </summary>
        Other = 10
    }
}
