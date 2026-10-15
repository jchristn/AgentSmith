namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Reveal a secret's values.
    /// </summary>
    public class SecretRevealRequest
    {
        #region Public-Members

        /// <summary>
        /// Why the values are needed (1 to 1024 characters; audited).
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        #endregion
    }
}
