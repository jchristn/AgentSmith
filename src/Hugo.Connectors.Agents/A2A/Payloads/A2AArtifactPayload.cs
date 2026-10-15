namespace Hugo.Connectors.Agents.A2A.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One artifact of a remote task. Text comes from the remote agent; binary parts are summarized. Not thread-safe.
    /// </summary>
    public class A2AArtifactPayload
    {
        /// <summary>
        /// Artifact identifier.
        /// </summary>
        [JsonPropertyName("artifactId")]
        public string ArtifactId { get; set; } = String.Empty;

        /// <summary>
        /// Name, or null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description, or null.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// Text of the text and data parts (clipped).
        /// </summary>
        [JsonPropertyName("text")]
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Summaries of file parts (name, media type, URL or size).
        /// </summary>
        [JsonPropertyName("files")]
        public List<string> Files { get; set; } = new List<string>();
    }
}
