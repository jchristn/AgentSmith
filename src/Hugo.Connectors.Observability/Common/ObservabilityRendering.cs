namespace Hugo.Connectors.Observability
{
    using System;

    /// <summary>
    /// Rendered output of an observability call: text for the model, structured JSON, and what was dropped.
    /// </summary>
    public class ObservabilityRendering
    {
        /// <summary>
        /// Text for the model.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Structured JSON object, or null.
        /// </summary>
        public string? StructuredJson { get; set; } = null;

        /// <summary>
        /// Whether items were dropped to respect a cap.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Items returned (series, lines, hits).
        /// </summary>
        public int Returned { get; set; } = 0;

        /// <summary>
        /// Items dropped by a cap.
        /// </summary>
        public int Omitted { get; set; } = 0;
    }
}
