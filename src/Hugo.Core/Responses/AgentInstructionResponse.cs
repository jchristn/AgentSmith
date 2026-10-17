namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// The current profile's persistent instructions and optimistic concurrency version.
    /// </summary>
    public class AgentInstructionResponse
    {
        /// <summary>
        /// Current profile identifier.
        /// </summary>
        public string ProfileId { get; set; } = String.Empty;

        /// <summary>
        /// Full persistent appended instructions, scrubbed before returning.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 version of the persisted text, suitable for ExpectedVersion.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Last profile update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
