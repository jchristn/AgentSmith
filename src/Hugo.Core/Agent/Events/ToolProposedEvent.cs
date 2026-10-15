namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>tool.proposed</c>: the model requested a tool call. Arguments are as the model produced them; the
    /// pipeline's redacted view arrives with <c>approval.required</c> when an approval is needed.
    /// </summary>
    public class ToolProposedEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "tool.proposed";
            }
        }

        /// <summary>
        /// Call ID.
        /// </summary>
        public string CallId { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Resource alias from the arguments, when present.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Arguments JSON.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Iteration (1-based).
        /// </summary>
        public int Iteration { get; set; } = 1;
    }
}
