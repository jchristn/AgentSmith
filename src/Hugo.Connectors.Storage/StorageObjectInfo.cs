namespace Hugo.Connectors.Storage
{
    using System;

    /// <summary>
    /// One object or folder entry returned by storage tools. Serialized as camelCase JSON into <c>ToolResult.StructuredJson</c>.
    /// </summary>
    public class StorageObjectInfo
    {
        /// <summary>
        /// Object key, or the folder prefix (ending with the delimiter) for folder entries.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Whether this entry is a folder (a common prefix, or a zero-byte key ending with '/').
        /// </summary>
        public bool IsFolder { get; set; } = false;

        /// <summary>
        /// Size in bytes. Zero for folders.
        /// </summary>
        public long Size { get; set; } = 0;

        /// <summary>
        /// Content type, when the provider reports it.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Entity tag, when the provider reports it.
        /// </summary>
        public string? ETag { get; set; } = null;

        /// <summary>
        /// Last modification time (UTC), when known.
        /// </summary>
        public DateTime? LastModifiedUtc { get; set; } = null;

        /// <summary>
        /// Creation time (UTC), when the provider reports it separately.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;
    }
}
