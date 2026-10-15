namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Change a password.
    /// </summary>
    public class PasswordChangeRequest
    {
        /// <summary>
        /// Current password; required when users change their own password.
        /// </summary>
        public string? CurrentPassword { get; set; } = null;

        /// <summary>
        /// New password. Minimum 8 characters, maximum 256, and different from the current one.
        /// </summary>
        public string NewPassword { get; set; } = String.Empty;
    }
}
