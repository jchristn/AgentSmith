namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>turn.started</c>: the runtime accepted the turn and holds the conversation lock.
    /// </summary>
    public class TurnStartedEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "turn.started";
            }
        }

        /// <summary>
        /// Surface (<c>chat</c>, <c>a2a</c>, <c>openai</c>, <c>mcp</c>).
        /// </summary>
        public string Surface { get; set; } = String.Empty;

        /// <summary>
        /// Profile ID, when known.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Principal ID, when known.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Primary inference endpoint key.
        /// </summary>
        public string EndpointKey { get; set; } = String.Empty;

        /// <summary>
        /// Primary model.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// ID of the persisted user message.
        /// </summary>
        public string? UserMessageId { get; set; } = null;
    }
}
