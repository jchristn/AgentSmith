namespace Hugo.Cli.Api
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The fields of an agent profile that <c>hugo profiles list</c> shows. Unknown fields are ignored.
    /// </summary>
    public class AgentProfileSummary
    {
        /// <summary>
        /// Profile ID, or null.
        /// </summary>
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Profile name (the <c>/mcp/{profileName}</c> path segment), or null.
        /// </summary>
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Display name, or null.
        /// </summary>
        [JsonConverter(typeof(LenientStringConverter))]
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Description, or null.
        /// </summary>
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Description { get; set; } = null;
    }
}
