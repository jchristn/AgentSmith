namespace Hugo.Core.Requests
{
    using System;

    /// <summary>Standalone chat StandaloneChatMessageRequest contract. Contains no credential or tool details.</summary>
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public class StandaloneChatMessageRequest
    {
        /// <summary>User message, at most 16384 characters.</summary>
        [System.Text.Json.Serialization.JsonRequired]
        public string Message { get; set; } = String.Empty;

    }
}
