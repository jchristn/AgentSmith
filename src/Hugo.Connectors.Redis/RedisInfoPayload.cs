namespace Hugo.Connectors.Redis
{
    using System.Collections.Generic;

    /// <summary>
    /// Structured content of <c>redis_info</c>. Not thread-safe for mutation.
    /// </summary>
    public class RedisInfoPayload
    {
        /// <summary>
        /// One entry per node reported.
        /// </summary>
        public List<RedisInfoNode> Nodes { get; set; } = new List<RedisInfoNode>();
    }
}
