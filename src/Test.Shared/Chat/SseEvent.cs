namespace Test.Shared.Chat
{
    using System;

    /// <summary>
    /// One Server-Sent Event read by a test.
    /// </summary>
    public class SseEvent
    {
        /// <summary>
        /// Event name, or null for an unnamed event.
        /// </summary>
        public string? Event { get; set; } = null;

        /// <summary>
        /// Event identifier, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Data (multi-line data joined with newlines).
        /// </summary>
        public string Data { get; set; } = String.Empty;
    }
}
