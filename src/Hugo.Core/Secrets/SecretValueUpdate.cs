namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Models;

    /// <summary>
    /// Result of a secret value update: the updated secret and the names of the fields that changed or were removed.
    /// </summary>
    public class SecretValueUpdate
    {
        #region Public-Members

        /// <summary>
        /// Updated secret.
        /// </summary>
        public Secret Secret { get; }

        /// <summary>
        /// Names of the fields that changed or were removed.
        /// </summary>
        public List<string> ChangedFields { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="secret">Secret.</param>
        /// <param name="changedFields">Changed field names.</param>
        /// <exception cref="ArgumentNullException">Thrown when secret is null.</exception>
        public SecretValueUpdate(Secret secret, List<string> changedFields)
        {
            Secret = secret ?? throw new ArgumentNullException(nameof(secret));
            ChangedFields = changedFields ?? new List<string>();
        }

        #endregion
    }
}
