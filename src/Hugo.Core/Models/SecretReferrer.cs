namespace Hugo.Core.Models
{
    using System;

    /// <summary>
    /// A record that references a secret (a resource secret slot or a model endpoint API key). A secret with referrers
    /// cannot be deleted.
    /// </summary>
    public class SecretReferrer
    {
        #region Public-Members

        /// <summary>
        /// Referrer type, for example Resource or ModelEndpoint.
        /// </summary>
        public string ReferrerType
        {
            get
            {
                return _ReferrerType;
            }
            set
            {
                _ReferrerType = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Referrer identifier.
        /// </summary>
        public string ReferrerId
        {
            get
            {
                return _ReferrerId;
            }
            set
            {
                _ReferrerId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Referrer display name, or null.
        /// </summary>
        public string? ReferrerName { get; set; } = null;

        /// <summary>
        /// Slot or field through which the secret is referenced, or null.
        /// </summary>
        public string? Detail { get; set; } = null;

        #endregion

        #region Private-Members

        private string _ReferrerType = String.Empty;
        private string _ReferrerId = String.Empty;

        #endregion
    }
}
