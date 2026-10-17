namespace Hugo.Core.Responses
{
    using System;

    /// <summary>Standalone chat StandaloneChatCancel contract. Contains no credential or tool details.</summary>
    public class StandaloneChatCancel
    {
        /// <summary>Whether an active turn was cancelled.</summary>
        public bool Cancelled { get; set; } = false;

    }
}
