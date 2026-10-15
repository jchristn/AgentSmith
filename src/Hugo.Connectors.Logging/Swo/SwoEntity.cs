namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A SolarWinds Observability entity (<c>Entities.Entity</c>), from <c>GET /v1/entities</c> or
    /// <c>GET /v1/entities/{id}</c>.
    /// </summary>
    public class SwoEntity
    {
        /// <summary>
        /// Entity ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Entity type (for example <c>Host</c>).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Display name; equals the name unless overridden.
        /// </summary>
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Creation time, UTC.
        /// </summary>
        [JsonPropertyName("createdTime")]
        public DateTimeOffset? CreatedTime { get; set; } = null;

        /// <summary>
        /// Last update time, UTC.
        /// </summary>
        [JsonPropertyName("updatedTime")]
        public DateTimeOffset? UpdatedTime { get; set; } = null;

        /// <summary>
        /// Time telemetry was last received, UTC.
        /// </summary>
        [JsonPropertyName("lastSeenTime")]
        public DateTimeOffset? LastSeenTime { get; set; } = null;

        /// <summary>
        /// Whether the entity is in maintenance mode.
        /// </summary>
        [JsonPropertyName("inMaintenance")]
        public bool InMaintenance { get; set; } = false;

        /// <summary>
        /// Health state, or null.
        /// </summary>
        [JsonPropertyName("healthState")]
        public SwoHealthState? HealthState { get; set; } = null;

        /// <summary>
        /// Tags (one value per key), or null.
        /// </summary>
        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Type-specific attributes. The set of keys differs per entity type, so values are kept as opaque JSON and
        /// rendered as text; they are never navigated.
        /// </summary>
        [JsonPropertyName("attributes")]
        public Dictionary<string, object?>? Attributes { get; set; } = null;
    }
}
