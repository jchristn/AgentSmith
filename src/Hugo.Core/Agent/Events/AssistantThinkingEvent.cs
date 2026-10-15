namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>assistant.thinking</c>: model reasoning, from inline <c>&lt;think&gt;</c> blocks or a provider reasoning field.
    /// </summary>
    public class AssistantThinkingEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "assistant.thinking";
            }
        }

        /// <summary>
        /// Reasoning fragment.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Iteration (1-based).
        /// </summary>
        public int Iteration { get; set; } = 1;
    }
}
