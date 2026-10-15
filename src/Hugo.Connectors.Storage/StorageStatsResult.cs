namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured result of storage_stats. Serialized as camelCase JSON into <c>ToolResult.StructuredJson</c>.
    /// </summary>
    public class StorageStatsResult
    {
        /// <summary>
        /// Bucket or container name.
        /// </summary>
        public string Container { get; set; } = String.Empty;

        /// <summary>
        /// Effective prefix scanned.
        /// </summary>
        public string Prefix { get; set; } = String.Empty;

        /// <summary>
        /// Objects counted.
        /// </summary>
        public long ObjectCount { get; set; } = 0;

        /// <summary>
        /// Total bytes counted.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Key of the largest object, or null when none.
        /// </summary>
        public string? LargestKey { get; set; } = null;

        /// <summary>
        /// Size of the largest object.
        /// </summary>
        public long LargestBytes { get; set; } = 0;

        /// <summary>
        /// Oldest modification time seen.
        /// </summary>
        public DateTime? OldestModifiedUtc { get; set; } = null;

        /// <summary>
        /// Newest modification time seen.
        /// </summary>
        public DateTime? NewestModifiedUtc { get; set; } = null;

        /// <summary>
        /// Whether the scan stopped at the cap, so counts are lower bounds.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Maximum objects this call would scan.
        /// </summary>
        public long ScanCap { get; set; } = 0;

        /// <summary>
        /// Per sub-prefix totals, largest first, when requested. Never null.
        /// </summary>
        public List<StorageStatsGroup> Groups { get; set; } = new List<StorageStatsGroup>();
    }
}
