namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>tool.started</c>: the call was handed to the invocation pipeline.
    /// </summary>
    public class ToolStartedEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "tool.started";
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
        /// Resource alias, when present.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Whether the call runs in parallel with other Read calls.
        /// </summary>
        public bool Parallel { get; set; } = false;

        /// <summary>
        /// Iteration (1-based).
        /// </summary>
        public int Iteration { get; set; } = 1;
    }
}
