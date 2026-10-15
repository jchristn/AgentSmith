namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One alerting or recording rule from <c>/api/v1/rules</c>.
    /// </summary>
    public class PromRule
    {
        /// <summary>
        /// Rule name (alert name or recorded metric).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// PromQL expression.
        /// </summary>
        public string? Query { get; set; } = null;

        /// <summary>
        /// <c>alerting</c> or <c>recording</c>.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Evaluation health: <c>ok</c>, <c>err</c>, or <c>unknown</c>.
        /// </summary>
        public string? Health { get; set; } = null;

        /// <summary>
        /// Alerting rule state: <c>inactive</c>, <c>pending</c>, or <c>firing</c>.
        /// </summary>
        public string? State { get; set; } = null;

        /// <summary>
        /// Last evaluation error.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Pending duration in seconds (alerting rules).
        /// </summary>
        public double? Duration { get; set; } = null;

        /// <summary>
        /// Rule labels.
        /// </summary>
        public Dictionary<string, string>? Labels { get; set; } = null;

        /// <summary>
        /// Active alerts of an alerting rule.
        /// </summary>
        public List<PromAlert>? Alerts { get; set; } = null;

        /// <summary>
        /// Last evaluation time.
        /// </summary>
        public DateTimeOffset? LastEvaluation { get; set; } = null;
    }
}
