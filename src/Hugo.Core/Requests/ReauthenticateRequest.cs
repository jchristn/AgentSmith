namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Re-enter the password on the current session.
    /// </summary>
    public class ReauthenticateRequest
    {
        /// <summary>
        /// Password. The x-password header is used when omitted.
        /// </summary>
        public string? Password { get; set; } = null;
    }
}
