namespace Hugo.Core.Responses
{
    using System;

    /// <summary>Standalone chat StandaloneChatInfo contract. Contains no credential or tool details.</summary>
    public class StandaloneChatInfo
    {
        /// <summary>Agent profile name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Display name.</summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>Visitor access policy.</summary>
        public Hugo.Core.Enums.ChatAccessModeEnum AccessMode { get; set; } = Hugo.Core.Enums.ChatAccessModeEnum.Authenticated;

        /// <summary>Whether a bearer session is required.</summary>
        public bool AuthenticationRequired { get; set; } = true;

    }
}
