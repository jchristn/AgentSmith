namespace Hugo.Core.Security
{
    using System;

    /// <summary>
    /// A loaded master key configuration.
    /// </summary>
    public class MasterKeyLoadResult
    {
        #region Public-Members

        /// <summary>
        /// Cipher holding the current and, when configured, the previous key.
        /// </summary>
        public SecretCipher Cipher { get; }

        /// <summary>
        /// Where the current key came from.
        /// </summary>
        public MasterKeySourceEnum Source { get; }

        /// <summary>
        /// Whether a previous key was configured.
        /// </summary>
        public bool HasPreviousKey { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="cipher">Cipher.</param>
        /// <param name="source">Source of the current key.</param>
        /// <param name="hasPreviousKey">Whether a previous key was configured.</param>
        /// <exception cref="ArgumentNullException">Thrown when cipher is null.</exception>
        public MasterKeyLoadResult(SecretCipher cipher, MasterKeySourceEnum source, bool hasPreviousKey)
        {
            Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            Source = source;
            HasPreviousKey = hasPreviousKey;
        }

        #endregion
    }
}
