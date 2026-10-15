namespace Hugo.Connectors.Storage
{
    using System;

    /// <summary>
    /// Structured result of storage_write, storage_copy, and storage_delete. Serialized as camelCase JSON into <c>ToolResult.StructuredJson</c>.
    /// </summary>
    public class StorageMutationResult
    {
        /// <summary>
        /// Operation: write, copy, or delete.
        /// </summary>
        public string Operation { get; set; } = String.Empty;

        /// <summary>
        /// Bucket or container name.
        /// </summary>
        public string Container { get; set; } = String.Empty;

        /// <summary>
        /// Key written, copied to, or deleted.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Source key for copies.
        /// </summary>
        public string? SourceKey { get; set; } = null;

        /// <summary>
        /// Bytes written, copied, or deleted.
        /// </summary>
        public long Bytes { get; set; } = 0;

        /// <summary>
        /// Whether an existing object was replaced.
        /// </summary>
        public bool Overwrote { get; set; } = false;

        /// <summary>
        /// Content type stored.
        /// </summary>
        public string? ContentType { get; set; } = null;
    }
}
