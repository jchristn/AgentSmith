namespace Hugo.Connectors.Storage
{
    using System;

    /// <summary>
    /// Object count and bytes for one sub-prefix in storage_stats. Serialized as camelCase JSON into <c>ToolResult.StructuredJson</c>.
    /// </summary>
    public class StorageStatsGroup
    {
        /// <summary>
        /// Sub-prefix (the next path segment under the requested prefix, ending with '/'), or the key of a top-level object.
        /// </summary>
        public string Prefix { get; set; } = String.Empty;

        /// <summary>
        /// Objects under the sub-prefix.
        /// </summary>
        public long ObjectCount { get; set; } = 0;

        /// <summary>
        /// Bytes under the sub-prefix.
        /// </summary>
        public long TotalBytes { get; set; } = 0;
    }
}
