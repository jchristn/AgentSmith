namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// A user message for a conversation turn (preview, verified when the Phase 7 conversation routes land).
    /// </summary>
    public class SendMessageRequest
    {
        #region Public-Members

        /// <summary>
        /// Message text.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        #endregion
    }
}
