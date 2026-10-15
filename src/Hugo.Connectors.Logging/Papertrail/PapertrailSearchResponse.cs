namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of Papertrail <c>GET /events/search.json</c>.
    /// </summary>
    public class PapertrailSearchResponse
    {
        /// <summary>
        /// Matching events, oldest first. May be null when the service omits it.
        /// </summary>
        [JsonPropertyName("events")]
        public List<PapertrailEvent>? Events { get; set; } = new List<PapertrailEvent>();

        /// <summary>
        /// Lowest event ID examined. Pass as <c>max_id</c> to page backward.
        /// </summary>
        [JsonPropertyName("min_id")]
        public string? MinId { get; set; } = null;

        /// <summary>
        /// Highest event ID examined. Pass as <c>min_id</c> to tail forward.
        /// </summary>
        [JsonPropertyName("max_id")]
        public string? MaxId { get; set; } = null;

        /// <summary>
        /// Oldest timestamp searched. Optional.
        /// </summary>
        [JsonPropertyName("min_time_at")]
        public DateTimeOffset? MinTimeAt { get; set; } = null;

        /// <summary>
        /// Newest timestamp searched. Optional.
        /// </summary>
        [JsonPropertyName("max_time_at")]
        public DateTimeOffset? MaxTimeAt { get; set; } = null;

        /// <summary>
        /// True when a backward search examined every match back to the start of retention.
        /// </summary>
        [JsonPropertyName("reached_beginning")]
        public bool ReachedBeginning { get; set; } = false;

        /// <summary>
        /// True when a forward search examined every match up to the present.
        /// </summary>
        [JsonPropertyName("reached_end")]
        public bool ReachedEnd { get; set; } = false;

        /// <summary>
        /// True when the result hit <c>limit</c>, so more matches may exist.
        /// </summary>
        [JsonPropertyName("reached_record_limit")]
        public bool ReachedRecordLimit { get; set; } = false;

        /// <summary>
        /// True when the server-side time budget ended the search early, so more matches may exist.
        /// </summary>
        [JsonPropertyName("reached_time_limit")]
        public bool ReachedTimeLimit { get; set; } = false;

        /// <summary>
        /// Whether older matches may exist beyond this page.
        /// </summary>
        [JsonIgnore]
        public bool HasOlder
        {
            get
            {
                return !ReachedBeginning && (ReachedRecordLimit || ReachedTimeLimit) && !String.IsNullOrEmpty(MinId);
            }
        }
    }
}
