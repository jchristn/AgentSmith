namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// One cached fetch of a resource's third-party guidance (<see cref="ThirdPartyInstructionCache"/>).
    /// </summary>
    public class ThirdPartyInstructionEntry
    {
        #region Public-Members

        /// <summary>
        /// Scrubbed, unfenced remote text, or null when the remote offered none or the fetch failed.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Connector error code when the fetch failed (for example timeout or connection_failed), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// When the fetch finished (UTC).
        /// </summary>
        public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
