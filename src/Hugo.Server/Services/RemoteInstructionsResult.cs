namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// Result of fetching a resource's remote instructions (<see cref="ResourceService.RemoteInstructionsAsync"/>).
    /// </summary>
    public class RemoteInstructionsResult
    {
        /// <summary>
        /// The remote text, scrubbed and wrapped in an untrusted-content fence, or null when there is none.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// The same remote text, scrubbed but not fenced (for callers that fence it themselves, such as the instruction
        /// compiler), or null when there is none.
        /// </summary>
        public string? ScrubbedText { get; set; } = null;

        /// <summary>
        /// Connector error code when fetching failed (for example timeout or connection_failed), or null.
        /// </summary>
        public string? Error { get; set; } = null;
    }
}
