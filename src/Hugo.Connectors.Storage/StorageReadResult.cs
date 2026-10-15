namespace Hugo.Connectors.Storage
{
    using System;

    /// <summary>
    /// Structured result of storage_read. Serialized as camelCase JSON into <c>ToolResult.StructuredJson</c>.
    /// </summary>
    public class StorageReadResult
    {
        /// <summary>
        /// Object key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content type reported by the provider.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Object size in bytes.
        /// </summary>
        public long Size { get; set; } = 0;

        /// <summary>
        /// First byte returned.
        /// </summary>
        public long Offset { get; set; } = 0;

        /// <summary>
        /// Bytes of object content returned (before base64 encoding).
        /// </summary>
        public long BytesReturned { get; set; } = 0;

        /// <summary>
        /// How Content is encoded: text or base64.
        /// </summary>
        public string Encoding { get; set; } = "text";

        /// <summary>
        /// Content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Whether more content exists after the returned range.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Offset to pass to continue reading, or null when the end was reached.
        /// </summary>
        public long? NextOffset { get; set; } = null;
    }
}
