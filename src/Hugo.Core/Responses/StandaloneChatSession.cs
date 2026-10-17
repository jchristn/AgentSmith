namespace Hugo.Core.Responses
{
    using System;

    /// <summary>Standalone chat StandaloneChatSession contract. Contains no credential or tool details.</summary>
    public class StandaloneChatSession
    {
        /// <summary>Conversation identifier.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Visitor capability returned once; send in x-hugo-chat-session.</summary>
        public string SessionToken { get; set; } = String.Empty;

    }
}
