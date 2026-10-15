namespace Hugo.Connectors.Logging
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A Papertrail group, from <c>GET /groups.json</c>.
    /// </summary>
    public class PapertrailGroup
    {
        /// <summary>
        /// Group ID.
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; } = 0;

        /// <summary>
        /// Group name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Wildcard matching system names, or null.
        /// </summary>
        [JsonPropertyName("system_wildcard")]
        public string? SystemWildcard { get; set; } = null;

        /// <summary>
        /// Member systems, or null.
        /// </summary>
        [JsonPropertyName("systems")]
        public List<PapertrailGroupSystem>? Systems { get; set; } = null;
    }
}
