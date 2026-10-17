namespace Hugo.Core.Responses
{
    using System;

    /// <summary>Standalone chat StandaloneChatMessages contract. Contains no credential or tool details.</summary>
    public class StandaloneChatMessages
    {
        /// <summary>Safe transcript.</summary>
        public System.Collections.Generic.List<StandaloneChatMessage> Messages { get; set; } = new System.Collections.Generic.List<StandaloneChatMessage>();

    }
}
