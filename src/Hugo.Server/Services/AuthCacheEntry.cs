namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// One cached authentication record and when it expires. Immutable.
    /// </summary>
    /// <typeparam name="T">Record type.</typeparam>
    public class AuthCacheEntry<T> where T : class
    {
        #region Public-Members

        /// <summary>
        /// Cached record. Never handed out directly; callers get a copy.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="value">Record.</param>
        /// <param name="expiresUtc">Expiry (UTC).</param>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        public AuthCacheEntry(T value, DateTime expiresUtc)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            ExpiresUtc = expiresUtc;
        }

        #endregion
    }
}
