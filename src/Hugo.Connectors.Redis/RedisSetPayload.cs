namespace Hugo.Connectors.Redis
{
    using System;

    /// <summary>
    /// Structured content of <c>redis_set</c>. Not thread-safe for mutation.
    /// </summary>
    public class RedisSetPayload
    {
        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Whether the value was written (false when the condition was not met).
        /// </summary>
        public bool Set { get; set; } = false;

        /// <summary>
        /// Value size in bytes.
        /// </summary>
        public long Bytes { get; set; } = 0;

        /// <summary>
        /// TTL applied, in seconds, or null.
        /// </summary>
        public long? TtlSeconds { get; set; } = null;

        /// <summary>
        /// Condition: always, ifNotExists, or ifExists.
        /// </summary>
        public string Condition { get; set; } = "always";
    }
}
