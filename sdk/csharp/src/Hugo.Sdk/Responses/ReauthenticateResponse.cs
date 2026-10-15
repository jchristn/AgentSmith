namespace Hugo.Sdk.Responses
{
    using System;

    /// <summary>
    /// Result of a re-authentication.
    /// </summary>
    public class ReauthenticateResponse
    {
        #region Public-Members

        /// <summary>
        /// Reauthenticated UTC.
        /// </summary>
        public DateTime ReauthenticatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Secrets can be revealed on this session until this time, UTC.
        /// </summary>
        public DateTime RevealAllowedUntilUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
