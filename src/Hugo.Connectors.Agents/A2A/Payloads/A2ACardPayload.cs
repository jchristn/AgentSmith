namespace Hugo.Connectors.Agents.A2A.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>a2a_get_card</c>. Text members come from the remote agent's card (third-party content). Not thread-safe.
    /// </summary>
    public class A2ACardPayload
    {
        /// <summary>
        /// Agent name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Agent description (clipped).
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Agent version, or null.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; } = null;

        /// <summary>
        /// Provider organization, or null.
        /// </summary>
        [JsonPropertyName("provider")]
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Documentation URL, or null.
        /// </summary>
        [JsonPropertyName("documentationUrl")]
        public string? DocumentationUrl { get; set; } = null;

        /// <summary>
        /// Where the card was resolved.
        /// </summary>
        [JsonPropertyName("cardUrl")]
        public string CardUrl { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint URL Hugo uses.
        /// </summary>
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = String.Empty;

        /// <summary>
        /// Protocol binding Hugo uses (JSONRPC or HTTP+JSON).
        /// </summary>
        [JsonPropertyName("binding")]
        public string Binding { get; set; } = String.Empty;

        /// <summary>
        /// Whether the card advertises streaming.
        /// </summary>
        [JsonPropertyName("streaming")]
        public bool Streaming { get; set; } = false;

        /// <summary>
        /// Whether a2a_delegate streams on this resource.
        /// </summary>
        [JsonPropertyName("streamingUsed")]
        public bool StreamingUsed { get; set; } = false;

        /// <summary>
        /// Accepted input media types.
        /// </summary>
        [JsonPropertyName("defaultInputModes")]
        public List<string> DefaultInputModes { get; set; } = new List<string>();

        /// <summary>
        /// Produced output media types.
        /// </summary>
        [JsonPropertyName("defaultOutputModes")]
        public List<string> DefaultOutputModes { get; set; } = new List<string>();

        /// <summary>
        /// The resource's skillId setting, or null.
        /// </summary>
        [JsonPropertyName("configuredSkillId")]
        public string? ConfiguredSkillId { get; set; } = null;

        /// <summary>
        /// Whether the configured skill is on the card, or null when none is configured.
        /// </summary>
        [JsonPropertyName("configuredSkillFound")]
        public bool? ConfiguredSkillFound { get; set; } = null;

        /// <summary>
        /// Skills on the card.
        /// </summary>
        [JsonPropertyName("skillCount")]
        public int SkillCount { get; set; } = 0;

        /// <summary>
        /// Skills (bounded by the row limit).
        /// </summary>
        [JsonPropertyName("skills")]
        public List<A2ASkillPayload> Skills { get; set; } = new List<A2ASkillPayload>();

        /// <summary>
        /// When the card was resolved (ISO 8601, UTC).
        /// </summary>
        [JsonPropertyName("fetchedUtc")]
        public string FetchedUtc { get; set; } = String.Empty;
    }
}
