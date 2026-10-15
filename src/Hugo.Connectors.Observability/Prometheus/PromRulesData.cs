namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;

    /// <summary>
    /// Data of <c>/api/v1/rules</c>.
    /// </summary>
    public class PromRulesData
    {
        /// <summary>
        /// Rule groups.
        /// </summary>
        public List<PromRuleGroup> Groups { get; set; } = new List<PromRuleGroup>();
    }
}
