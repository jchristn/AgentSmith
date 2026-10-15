namespace Test.Shared.Agent.StubLlm
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A request the stub LLM received, for assertions.
    /// </summary>
    public class StubRecordedRequest
    {
        /// <summary>
        /// Path (for example <c>/v1/chat/completions</c>).
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Raw body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Model requested.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Whether streaming was requested.
        /// </summary>
        public bool Stream { get; set; } = false;

        /// <summary>
        /// Tool choice as sent, or null.
        /// </summary>
        public string? ToolChoice { get; set; } = null;

        /// <summary>
        /// Names of the tools offered.
        /// </summary>
        public List<string> ToolNames { get; set; } = new List<string>();

        /// <summary>
        /// Chat messages (chat requests).
        /// </summary>
        public List<StubChatMessage> Messages { get; set; } = new List<StubChatMessage>();

        /// <summary>
        /// Embedding inputs (embedding requests).
        /// </summary>
        public List<string> Inputs { get; set; } = new List<string>();

        /// <summary>
        /// When the request arrived (UTC).
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
    }
}
