namespace Test.Shared.Connectors.Observability
{
    using System.Collections.Generic;

    /// <summary>
    /// Index guard table: requested expressions against allow lists, with the expected outcome.
    /// </summary>
    public static class EsIndexGuardTable
    {
        /// <summary>
        /// Rows.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<EsIndexGuardCase> Cases()
        {
            string logs = "logs-*";
            string two = "logs-*\nmetrics-2026.*";
            return new List<EsIndexGuardCase>
            {
                new EsIndexGuardCase("logs-2026.10", logs, "A"),
                new EsIndexGuardCase("logs-*", logs, "A"),
                new EsIndexGuardCase("logs-2026.*", logs, "A"),
                new EsIndexGuardCase("logs-*-prod", logs, "A"),
                new EsIndexGuardCase("logs-a,logs-b", logs, "A"),
                new EsIndexGuardCase(" logs-a , logs-b ", logs, "A"),
                new EsIndexGuardCase("logs-", logs, "A"),
                new EsIndexGuardCase("logs", logs, "D"),
                new EsIndexGuardCase("log*", logs, "D"),
                new EsIndexGuardCase("l*", logs, "D"),
                new EsIndexGuardCase("*", logs, "D"),
                new EsIndexGuardCase("*-logs", logs, "D"),
                new EsIndexGuardCase("*logs-*", logs, "D"),
                new EsIndexGuardCase("logs-a,secret", logs, "D"),
                new EsIndexGuardCase("secret", logs, "D"),
                new EsIndexGuardCase("metrics-2026.10", two, "A"),
                new EsIndexGuardCase("metrics-2026.*", two, "A"),
                new EsIndexGuardCase("metrics-*", two, "D"),
                new EsIndexGuardCase("metrics-2025.01", two, "D"),
                new EsIndexGuardCase("logs-x,metrics-2026.01", two, "A"),
                new EsIndexGuardCase("anything", "", "A"),
                new EsIndexGuardCase("*", "", "A"),
                new EsIndexGuardCase("logs-*,metrics", "", "A"),
                new EsIndexGuardCase(".kibana", "", "D"),
                new EsIndexGuardCase(".kibana", "*", "D"),
                new EsIndexGuardCase(".ds-logs-1", logs, "D"),
                new EsIndexGuardCase(".kibana", ".kib*", "A"),
                new EsIndexGuardCase(".*", ".kib*", "D"),
                new EsIndexGuardCase(".kibana_1", ".*", "A"),
                new EsIndexGuardCase("logs-1", ".*", "D"),
                new EsIndexGuardCase("_all", "", "D"),
                new EsIndexGuardCase("_all", "*", "D"),
                new EsIndexGuardCase("_cat", "*", "D"),
                new EsIndexGuardCase("remote:logs-1", "*", "D"),
                new EsIndexGuardCase("cluster*:logs-*", logs, "D"),
                new EsIndexGuardCase("-logs-1", "*", "I"),
                new EsIndexGuardCase("logs-*,-logs-secret", logs, "I"),
                new EsIndexGuardCase("+logs-1", "*", "I"),
                new EsIndexGuardCase("<logs-{now/d}>", "*", "I"),
                new EsIndexGuardCase("logs-{now}", "*", "I"),
                new EsIndexGuardCase("logs 1", "*", "I"),
                new EsIndexGuardCase("Logs-1", "*", "I"),
                new EsIndexGuardCase("logs/1", "*", "I"),
                new EsIndexGuardCase("logs?1", "*", "I"),
                new EsIndexGuardCase("logs#1", "*", "I"),
                new EsIndexGuardCase("logs\"1", "*", "I"),
                new EsIndexGuardCase("logs\\1", "*", "I"),
                new EsIndexGuardCase("logs-1,", "*", "I"),
                new EsIndexGuardCase(",logs-1", "*", "I"),
                new EsIndexGuardCase("logs-1,,logs-2", "*", "I"),
                new EsIndexGuardCase("", "*", "I"),
                new EsIndexGuardCase("   ", "*", "I"),
                new EsIndexGuardCase("../_cluster", "*", "I"),
                new EsIndexGuardCase("logs-1%2f..", "*", "I")
            };
        }
    }
}
