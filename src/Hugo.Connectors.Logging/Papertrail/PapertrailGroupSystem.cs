namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A system listed inside a Papertrail group (<c>systems[]</c>). Only the identifying fields are read.
    /// </summary>
    public class PapertrailGroupSystem
    {
        /// <summary>
        /// System ID.
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; } = 0;

        /// <summary>
        /// System name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;
    }
}
