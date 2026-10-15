namespace Hugo.Cli.Mcp
{
    /// <summary>
    /// Progress of one event stream being relayed: whether the awaited response arrived, and where to resume.
    /// Not thread-safe.
    /// </summary>
    public class SseRelayState
    {
        /// <summary>
        /// Whether the response to the request arrived on the stream.
        /// </summary>
        public bool GotResponse { get; set; } = false;

        /// <summary>
        /// ID of the last event received, used as <c>Last-Event-ID</c> when resuming, or null.
        /// </summary>
        public string? LastEventId { get; set; } = null;

        /// <summary>
        /// The server's requested reconnection delay in milliseconds, or null.
        /// </summary>
        public int? RetryMs { get; set; } = null;

        /// <summary>
        /// Messages written to standard output from this stream.
        /// </summary>
        public int MessagesRelayed { get; set; } = 0;
    }
}
