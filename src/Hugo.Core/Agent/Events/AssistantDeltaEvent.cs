namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>assistant.delta</c>: visible answer text, with think blocks removed.
    /// </summary>
    public class AssistantDeltaEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "assistant.delta";
            }
        }

        /// <summary>
        /// Text fragment.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Iteration (1-based).
        /// </summary>
        public int Iteration { get; set; } = 1;
    }
}
