namespace Hugo.Core.Agent
{
    using System;

    /// <summary>
    /// A tool call requested by an assistant message.
    /// </summary>
    public class AgentMessageToolCall
    {
        /// <summary>
        /// Call ID.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Arguments JSON.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";
    }
}
