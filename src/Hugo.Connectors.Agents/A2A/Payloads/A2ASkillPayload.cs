namespace Hugo.Connectors.Agents.A2A.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One skill of a remote agent card. Not thread-safe.
    /// </summary>
    public class A2ASkillPayload
    {
        /// <summary>
        /// Skill identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Skill name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description (clipped).
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Tags.
        /// </summary>
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Example requests.
        /// </summary>
        [JsonPropertyName("examples")]
        public List<string> Examples { get; set; } = new List<string>();
    }
}
