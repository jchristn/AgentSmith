namespace Hugo.Cli.Api
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of <c>GET /v1.0/api/agent-profiles</c> (the standard enumeration envelope).
    /// </summary>
    public class AgentProfileListResponse
    {
        /// <summary>
        /// The response body exactly as received (for <c>--json</c> output). Not serialized.
        /// </summary>
        [JsonIgnore]
        public string RawJson { get; set; } = String.Empty;

        /// <summary>
        /// Profiles on this page. Never null.
        /// </summary>
        public List<AgentProfileSummary> Objects { get; set; } = new List<AgentProfileSummary>();

        /// <summary>
        /// Total profiles visible to the caller.
        /// </summary>
        public long TotalRecords { get; set; } = 0;
    }
}
