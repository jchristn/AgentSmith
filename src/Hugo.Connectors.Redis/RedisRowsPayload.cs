namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured content of table-shaped results (slow log, clients, latency, TTLs, memory, big keys): column names and
    /// rows of cell text. Not thread-safe for mutation.
    /// </summary>
    public class RedisRowsPayload
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        public string Tool { get; set; } = String.Empty;

        /// <summary>
        /// Column names.
        /// </summary>
        public List<string> Columns { get; set; } = new List<string>();

        /// <summary>
        /// Rows; each row has one cell per column, null for no value.
        /// </summary>
        public List<List<string?>> Rows { get; set; } = new List<List<string?>>();

        /// <summary>
        /// Rows available before limits were applied.
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// Whether rows were dropped to fit the limits.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Extra facts (for example the latency monitor threshold or the number of keys sampled).
        /// </summary>
        public Dictionary<string, string>? Notes { get; set; } = null;
    }
}
