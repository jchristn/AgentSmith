namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;

    /// <summary>
    /// Data of <c>/api/v1/alerts</c>.
    /// </summary>
    public class PromAlertsData
    {
        /// <summary>
        /// Active alerts.
        /// </summary>
        public List<PromAlert> Alerts { get; set; } = new List<PromAlert>();
    }
}
