namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Models;

    /// <summary>
    /// A revealed secret: its metadata and plaintext values. Only ever returned by the audited reveal route.
    /// </summary>
    public class SecretReveal
    {
        #region Public-Members

        /// <summary>
        /// Secret metadata.
        /// </summary>
        public Secret Secret { get; }

        /// <summary>
        /// Plaintext values by field name.
        /// </summary>
        public Dictionary<string, string> Values { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="secret">Secret.</param>
        /// <param name="values">Plaintext values.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SecretReveal(Secret secret, Dictionary<string, string> values)
        {
            Secret = secret ?? throw new ArgumentNullException(nameof(secret));
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        #endregion
    }
}
