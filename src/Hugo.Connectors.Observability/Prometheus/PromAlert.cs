namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One alert from <c>/api/v1/alerts</c> or inside an alerting rule.
    /// </summary>
    public class PromAlert
    {
        /// <summary>
        /// Labels, including <c>alertname</c>.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Annotations (summary, description, runbook).
        /// </summary>
        public Dictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// State: <c>pending</c> or <c>firing</c>.
        /// </summary>
        public string? State { get; set; } = null;

        /// <summary>
        /// When the alert became active.
        /// </summary>
        public DateTimeOffset? ActiveAt { get; set; } = null;

        /// <summary>
        /// Value of the alert expression when last evaluated.
        /// </summary>
        public string? Value { get; set; } = null;
    }
}
