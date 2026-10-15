namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured result of storage_list. Serialized as camelCase JSON into <c>ToolResult.StructuredJson</c>.
    /// </summary>
    public class StorageListResult
    {
        /// <summary>
        /// Bucket or container name.
        /// </summary>
        public string Container { get; set; } = String.Empty;

        /// <summary>
        /// Effective prefix listed.
        /// </summary>
        public string Prefix { get; set; } = String.Empty;

        /// <summary>
        /// Delimiter used to group folders, or null for a flat listing.
        /// </summary>
        public string? Delimiter { get; set; } = null;

        /// <summary>
        /// Entries in lexicographic order. Never null.
        /// </summary>
        public List<StorageObjectInfo> Entries { get; set; } = new List<StorageObjectInfo>();

        /// <summary>
        /// Number of object entries.
        /// </summary>
        public int ObjectCount { get; set; } = 0;

        /// <summary>
        /// Number of folder entries.
        /// </summary>
        public int FolderCount { get; set; } = 0;

        /// <summary>
        /// Token for the next page, or null when the listing is complete.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Objects examined by this call after the continuation point (returned entries plus keys folded into folders).
        /// </summary>
        public long Scanned { get; set; } = 0;

        /// <summary>
        /// Keys this call re-walked and skipped because earlier pages already covered them. Zero on the first page.
        /// </summary>
        public long Skipped { get; set; } = 0;

        /// <summary>
        /// Whether the scan stopped at maxScanObjects before the page filled; continue with the token.
        /// </summary>
        public bool ScanTruncated { get; set; } = false;
    }
}
