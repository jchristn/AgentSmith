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
        /// Effective denied commands for this case. Starts with the built-in KEYS; a <c>deny=</c> option adds to it, as
        /// <c>deniedCommands</c> does, and <c>allowkeys</c> removes KEYS, as <c>allowKeysCommand</c> does.
        /// </summary>
        public List<string> Denied { get; set; } = new List<string> { "KEYS" };

        /// <summary>
        /// Extra read-only commands for this case.
        /// </summary>
        public List<string> ReadOnly { get; set; } = new List<string>();
    }
}
