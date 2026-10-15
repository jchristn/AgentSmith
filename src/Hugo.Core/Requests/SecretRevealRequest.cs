namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Reveal a secret's plaintext values. The reason is recorded in the audit trail.
    /// </summary>
    public class SecretRevealRequest
    {
        /// <summary>
        /// Why the values are needed. Required; 1 to 1024 characters.
        /// </summary>
        public string Reason { get; set; } = String.Empty;
    }
}
