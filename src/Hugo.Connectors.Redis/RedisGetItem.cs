namespace Hugo.Connectors.Redis
{
    using System.Collections.Generic;

    /// <summary>
    /// One element of a collection read by <c>redis_get</c>: a list element (index), hash field, set member, sorted set
    /// member with score, or stream entry (ID and fields). Not thread-safe for mutation.
    /// </summary>
    public class RedisGetItem
    {
        /// <summary>
        /// List index or sorted set rank, when applicable.
        /// </summary>
        public long? Index { get; set; } = null;

        /// <summary>
        /// Hash field name.
        /// </summary>
        public string? Field { get; set; } = null;

        /// <summary>
        /// Sorted set score.
        /// </summary>
        public double? Score { get; set; } = null;

        /// <summary>
        /// Stream entry ID.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Value (list element, hash value, set or sorted set member).
        /// </summary>
        public RedisDisplayValue? Value { get; set; } = null;

        /// <summary>
        /// Stream entry fields.
        /// </summary>
        public Dictionary<string, RedisDisplayValue>? Fields { get; set; } = null;
    }
}
