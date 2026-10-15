namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The group a Papertrail saved search is scoped to (<c>group</c> object).
    /// </summary>
    public class PapertrailSavedSearchGroup
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
    }
}
