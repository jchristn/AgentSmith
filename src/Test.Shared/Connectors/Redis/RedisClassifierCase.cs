namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One row of the Redis command classifier table.
    /// </summary>
    public class RedisClassifierCase
    {
        /// <summary>
        /// Expected outcome: R, W, D, A (Destructive that always requires approval), or X (rejected).
        /// </summary>
        public string Expected { get; set; } = "D";

        /// <summary>
        /// Command line.
        /// </summary>
        public string Command { get; set; } = String.Empty;

        /// <summary>
        /// Denied commands for this case. Defaults to the connector default (KEYS).
        /// </summary>
        public List<string> Denied { get; set; } = new List<string> { "KEYS" };

        /// <summary>
        /// Extra read-only commands for this case.
        /// </summary>
        public List<string> ReadOnly { get; set; } = new List<string>();
    }
}
