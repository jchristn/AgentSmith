namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Result of a successful re-authentication.
    /// </summary>
    public class ReauthenticateResponse
    {
        /// <summary>
        /// When the session was re-authenticated (UTC).
        /// </summary>
        public DateTime ReauthenticatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Until when the session may reveal secrets (UTC).
        /// </summary>
        public DateTime RevealAllowedUntilUtc { get; set; } = DateTime.UtcNow;
    }
}
