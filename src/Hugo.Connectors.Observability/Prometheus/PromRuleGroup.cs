namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;

    /// <summary>
    /// One rule group from <c>/api/v1/rules</c>.
    /// </summary>
    public class PromRuleGroup
    {
        /// <summary>
        /// Group name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Rule file.
        /// </summary>
        public string? File { get; set; } = null;

        /// <summary>
        /// Evaluation interval in seconds.
        /// </summary>
        public double? Interval { get; set; } = null;

        /// <summary>
        /// Rules.
        /// </summary>
        public List<PromRule> Rules { get; set; } = new List<PromRule>();
    }
}
