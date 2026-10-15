namespace Hugo.Connectors.Redis
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using StackExchange.Redis;

    /// <summary>
    /// One bounded page of a SCAN walk, and the structured content of <c>redis_scan</c>. Not thread-safe for mutation.
    /// </summary>
    public class RedisScanPage
    {
        /// <summary>
        /// MATCH pattern.
        /// </summary>
        public string Pattern { get; set; } = "*";

        /// <summary>
        /// TYPE filter, or null.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Keys found, in scan order, without duplicates.
        /// </summary>
        public List<RedisScanKey> Keys { get; set; } = new List<RedisScanKey>();

        /// <summary>
        /// Raw keys, parallel to <see cref="Keys"/>, for follow-up commands. Not serialized.
        /// </summary>
        [JsonIgnore]
        public List<RedisKey> RawKeys { get; set; } = new List<RedisKey>();

        /// <summary>
        /// Index of the primary each key came from, parallel to <see cref="Keys"/>. Not serialized.
        /// </summary>
        [JsonIgnore]
        public List<int> NodeIndexes { get; set; } = new List<int>();

        /// <summary>
        /// Approximate number of keys examined (the sum of SCAN COUNT hints).
        /// </summary>
        public long Scanned { get; set; } = 0;

        /// <summary>
        /// Whether the walk reached the end of the keyspace.
        /// </summary>
        public bool Complete { get; set; } = false;

        /// <summary>
        /// Whether the walk stopped because it examined the maximum number of keys.
        /// </summary>
        public bool ScanLimitReached { get; set; } = false;

        /// <summary>
        /// Cursor to continue the walk with the same pattern and type, or null when complete.
        /// </summary>
        public string? NextCursor { get; set; } = null;

        /// <summary>
        /// Keys in the database (DBSIZE summed over primaries), when known.
        /// </summary>
        public long? DatabaseSize { get; set; } = null;
    }
}
