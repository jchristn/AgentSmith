namespace Hugo.Core.Responses
{
    using System;

    /// <summary>Standalone chat StandaloneChatMessages contract. Contains no credential or tool details.</summary>
    public class StandaloneChatMessages
    {
        /// <summary>Whether the session has a turn running, including while its viewer is disconnected.</summary>
        public bool TurnInProgress { get; set; } = false;

        /// <summary>Safe persisted visitor messages.</summary>
        public System.Collections.Generic.List<StandaloneChatMessage> Messages { get; set; } = new System.Collections.Generic.List<StandaloneChatMessage>();

    }
}
