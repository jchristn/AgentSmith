namespace Hugo.Core.Agent
{
    using System;

    /// <summary>
    /// Another turn is already running on the conversation (P6.20). Surfaces map it to HTTP 409.
    /// </summary>
    public class AgentTurnConflictException : Exception
    {
        /// <summary>
        /// Conversation ID.
        /// </summary>
        public string ConversationId { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        public AgentTurnConflictException(string conversationId)
            : base("Another turn is already running on conversation " + conversationId + ". Wait for it to finish or cancel it.")
        {
            ConversationId = conversationId ?? String.Empty;
        }
    }
}
