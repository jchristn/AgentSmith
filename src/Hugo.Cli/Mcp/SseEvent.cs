namespace Hugo.Cli.Mcp
{
    using System;

    /// <summary>
    /// One server-sent event.
    /// </summary>
    public class SseEvent
    {
        /// <summary>
        /// Event ID (<c>id:</c>), or null when the event had none.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Event type (<c>event:</c>); <c>message</c> when absent.
        /// </summary>
        public string EventType { get; set; } = "message";

        /// <summary>
        /// Data (<c>data:</c> lines joined with LF). Empty for priming events.
        /// </summary>
        public string Data { get; set; } = String.Empty;

        /// <summary>
        /// Reconnection delay in milliseconds (<c>retry:</c>), or null.
        /// </summary>
        public int? RetryMs { get; set; } = null;
    }
}
