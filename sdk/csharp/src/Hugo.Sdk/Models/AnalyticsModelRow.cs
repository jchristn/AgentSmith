namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Per-model-endpoint analytics row.
    /// </summary>
    public class AnalyticsModelRow
    {
        #region Public-Members

        /// <summary>
        /// Model endpoint identifier.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Provider.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Model name.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Agent turns.
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
        /// Average time to first token, milliseconds.
        /// </summary>
        public double AvgTimeToFirstTokenMs { get; set; } = 0;

        /// <summary>
        /// Average turn duration, milliseconds.
        /// </summary>
        public double AvgTurnDurationMs { get; set; } = 0;

        #endregion
    }
}
