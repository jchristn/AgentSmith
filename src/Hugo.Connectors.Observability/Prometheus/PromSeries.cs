namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One series of a vector (<see cref="Value"/>) or matrix (<see cref="Values"/>) result.
    /// </summary>
    public class PromSeries
    {
        /// <summary>
        /// Label set, including <c>__name__</c> when present.
        /// </summary>
        public Dictionary<string, string> Metric { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Sample of an instant vector.
        /// </summary>
        public PromPoint? Value { get; set; } = null;

        /// <summary>
        /// Samples of a range matrix.
        /// </summary>
        public List<PromPoint>? Values { get; set; } = null;
    }
}
