namespace Hugo.Core.Requests
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Change the current agent profile's persistent appended instructions.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public class AgentInstructionRequest
    {
        /// <summary>
        /// New instructions, or a contribution to append. Maximum 65536 characters including existing content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// append (default) or replace. Replace requires ExpectedVersion; empty replacement clears the instructions.
        /// </summary>
        public string Mode { get; set; } = "append";

        /// <summary>
        /// SHA-256 version returned by the read operation. Optional for append, required for replace.
        /// </summary>
        public string? ExpectedVersion { get; set; } = null;
    }
}
