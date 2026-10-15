namespace Hugo.Connectors.Agents.A2A.Payloads
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>a2a_delegate</c> and <c>a2a_get_task</c>. Message and artifact text come from the remote agent. Not thread-safe.
    /// </summary>
    public class A2ATaskPayload
    {
        /// <summary>
        /// Remote agent name.
        /// </summary>
        [JsonPropertyName("agent")]
        public string Agent { get; set; } = String.Empty;

        /// <summary>
        /// Task state: submitted, working, completed, failed, canceled, rejected, input-required, auth-required, or unknown. A direct message reply is completed.
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Whether the task reached a final state.
        /// </summary>
        [JsonPropertyName("terminal")]
        public bool Terminal { get; set; } = false;

        /// <summary>
        /// Whether the remote agent is waiting for more input (input-required).
        /// </summary>
        [JsonPropertyName("needsInput")]
        public bool NeedsInput { get; set; } = false;

        /// <summary>
        /// Task identifier, or null for a direct message reply.
        /// </summary>
        [JsonPropertyName("taskId")]
        public string? TaskId { get; set; } = null;

        /// <summary>
        /// Context identifier, used to continue the conversation.
        /// </summary>
        [JsonPropertyName("contextId")]
        public string? ContextId { get; set; } = null;

        /// <summary>
        /// The agent's latest message text (clipped), or null.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        /// <summary>
        /// Artifacts (bounded).
        /// </summary>
        [JsonPropertyName("artifacts")]
        public List<A2AArtifactPayload> Artifacts { get; set; } = new List<A2AArtifactPayload>();

        /// <summary>
        /// Artifacts the task has.
        /// </summary>
        [JsonPropertyName("artifactCount")]
        public int ArtifactCount { get; set; } = 0;

        /// <summary>
        /// What the caller should do next, or null.
        /// </summary>
        [JsonPropertyName("next")]
        public string? Next { get; set; } = null;
    }
}
