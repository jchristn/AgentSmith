namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured content of <c>redis_get</c>. Not thread-safe for mutation.
    /// </summary>
    public class RedisGetPayload
    {
        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Type (string, hash, list, set, zset, stream, or a module type).
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Remaining time to live in milliseconds; -1 for no expiry.
        /// </summary>
        public long TtlMs { get; set; } = -1;

        /// <summary>
        /// Size: bytes for a string, element count for a collection.
        /// </summary>
        public long Length { get; set; } = 0;

        /// <summary>
        /// First position returned (list index, sorted set rank, string byte offset).
        /// </summary>
        public long? Offset { get; set; } = null;

        /// <summary>
        /// Elements returned.
        /// </summary>
        public int Returned { get; set; } = 0;

        /// <summary>
        /// Continuation for the next page: an offset, a scan cursor (hash, set), or an exclusive stream ID. Null when the
        /// page reached the end.
        /// </summary>
        public string? Next { get; set; } = null;

        /// <summary>
        /// Value of a string key.
        /// </summary>
        public RedisDisplayValue? Value { get; set; } = null;

        /// <summary>
        /// Elements of a collection key.
        /// </summary>
        public List<RedisGetItem>? Items { get; set; } = null;

        /// <summary>
        /// Whether values were cut to maxValueBytes or elements omitted.
        /// </summary>
        public bool Truncated { get; set; } = false;
    }
}
