namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One data stream in a <c>_resolve/index</c> response.
    /// </summary>
    public class EsResolvedDataStream
    {
        /// <summary>
        /// Data stream name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Backing indexes of the data stream.
        /// </summary>
        [JsonPropertyName("backing_indices")]
        public List<string> BackingIndices { get; set; } = new List<string>();
    }
}
