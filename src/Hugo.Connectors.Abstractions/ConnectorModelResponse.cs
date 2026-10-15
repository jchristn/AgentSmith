namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// The result of a connector model request.
    /// </summary>
    public class ConnectorModelResponse
    {
        /// <summary>
        /// Completion text with any reasoning blocks removed.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Model identifier that produced the text (for provenance in knowledge entries).
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Input tokens consumed.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens produced.
        /// </summary>
        public long OutputTokens { get; set; } = 0;
    }
}
