namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Model endpoint usage over an analytics window. Not thread-safe.
    /// </summary>
    public class AnalyticsModelRow
    {
        /// <summary>
        /// Model endpoint identifier, or null for messages without one.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Endpoint name, when it still exists.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Provider, when the endpoint still exists.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Model, when the endpoint still exists.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Turns answered.
        /// </summary>
        public long Turns { get; set; } = 0;

        /// <summary>
        /// Input tokens.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Mean time to first token in milliseconds, or null.
        /// </summary>
        public double? AvgTimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Mean turn duration in milliseconds, or null.
        /// </summary>
        public double? AvgTurnDurationMs { get; set; } = null;
    }
}
