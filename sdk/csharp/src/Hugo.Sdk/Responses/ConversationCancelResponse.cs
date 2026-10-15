namespace Hugo.Sdk.Responses
{
    using System;

    /// <summary>
    /// Result of cancelling a conversation's running turn.
    /// </summary>
    public class ConversationCancelResponse
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Whether a running turn was cancelled (false when none was running).
        /// </summary>
        public bool Cancelled { get; set; } = false;

        #endregion
    }
}
