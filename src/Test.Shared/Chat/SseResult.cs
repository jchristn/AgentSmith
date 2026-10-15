namespace Test.Shared.Chat
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A streamed response read by a test: status, content type, headers, and events (or the body when it was not a
    /// stream).
    /// </summary>
    public class SseResult
    {
        /// <summary>
        /// HTTP status.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Media type of the response.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Response headers, lowercase names.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Events, in order.
        /// </summary>
        public List<SseEvent> Events { get; set; } = new List<SseEvent>();

        /// <summary>
        /// Body, when the response was not an event stream.
        /// </summary>
        public string Body { get; set; } = String.Empty;
    }
}
