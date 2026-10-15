namespace Hugo.Connectors.Storage
{
    using System;

    /// <summary>
    /// Structured result of storage_presign_url. Serialized as camelCase JSON into <c>ToolResult.StructuredJson</c>.
    /// </summary>
    public class StoragePresignResult
    {
        /// <summary>
        /// Bucket or container name.
        /// </summary>
        public string Container { get; set; } = String.Empty;

        /// <summary>
        /// Object key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// HTTP method the URL authorizes (GET or PUT).
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Signed URL. Anyone holding it has the granted access until it expires.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Expiry time (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.MinValue;
    }
}
