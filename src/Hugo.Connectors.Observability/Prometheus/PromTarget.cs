namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One scrape target from <c>/api/v1/targets</c>.
    /// </summary>
    public class PromTarget
    {
        /// <summary>
        /// Labels after relabeling (active targets).
        /// </summary>
        public Dictionary<string, string>? Labels { get; set; } = null;

        /// <summary>
        /// Labels before relabeling.
        /// </summary>
        public Dictionary<string, string>? DiscoveredLabels { get; set; } = null;

        /// <summary>
        /// Scrape pool (job).
        /// </summary>
        public string? ScrapePool { get; set; } = null;

        /// <summary>
        /// Scrape URL.
        /// </summary>
        public string? ScrapeUrl { get; set; } = null;

        /// <summary>
        /// Health: <c>up</c>, <c>down</c>, or <c>unknown</c>.
        /// </summary>
        public string? Health { get; set; } = null;

        /// <summary>
        /// Last scrape error.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Last scrape time.
        /// </summary>
        public DateTimeOffset? LastScrape { get; set; } = null;

        /// <summary>
        /// Last scrape duration in seconds.
        /// </summary>
        public double? LastScrapeDuration { get; set; } = null;
    }
}
