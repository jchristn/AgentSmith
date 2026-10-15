namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Change or reset a password.
    /// </summary>
    public class PasswordChangeRequest
    {
        #region Public-Members

        /// <summary>
        /// Current password (required for a self change).
        /// </summary>
        public string? CurrentPassword { get; set; } = null;

        /// <summary>
        /// New password, 8 to 256 characters.
        /// </summary>
        public string NewPassword { get; set; } = String.Empty;

        #endregion
    }
}
