namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A tool call requested by an assistant message in a conversation.
    /// </summary>
    public class ConversationToolCall
    {
        #region Public-Members

        /// <summary>
        /// Call identifier.
        /// </summary>
        public string CallId { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as JSON text.
        /// </summary>
        public string ArgumentsJson { get; set; } = String.Empty;

        #endregion
    }
}
