namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One stream of a Loki <c>streams</c> result.
    /// </summary>
    public class LokiStream
    {
        /// <summary>
        /// Stream labels.
        /// </summary>
        public Dictionary<string, string> Stream { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Entries.
        /// </summary>
        public List<LokiEntry> Values { get; set; } = new List<LokiEntry>();
    }
}
