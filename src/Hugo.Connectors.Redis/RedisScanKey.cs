namespace Hugo.Connectors.Redis
{
    using System;

    /// <summary>
    /// One key found by a scan. Not thread-safe for mutation.
    /// </summary>
    public class RedisScanKey
    {
        /// <summary>
        /// Key name (printable form; binary keys as <c>base64:...</c>).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Key type (string, hash, list, set, zset, stream, or a module type), when known.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Remaining time to live in milliseconds; -1 for no expiry; null when unknown.
        /// </summary>
        public long? TtlMs { get; set; } = null;

        /// <summary>
        /// Node the key lives on (cluster only).
        /// </summary>
        public string? Node { get; set; } = null;
    }
}
