namespace Hugo.Sdk.Responses
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>
    /// A created or rotated credential with its secret key (returned once).
    /// </summary>
    public class CredentialCreateResponse
    {
        #region Public-Members

        /// <summary>
        /// Credential.
        /// </summary>
        public Credential Credential { get; set; } = new Credential();

        /// <summary>
        /// Access key.
        /// </summary>
        public string AccessKey { get; set; } = String.Empty;

        /// <summary>
        /// Secret key. Store it now; it is never returned again.
        /// </summary>
        public string SecretKey { get; set; } = String.Empty;

        #endregion
    }
}
