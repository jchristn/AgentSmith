namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Signed-request nonces shared by every control-plane replica (P10.2).
    /// </summary>
    public interface IRequestNonceMethods
    {
        /// <summary>
        /// Record a nonce. Returns false when the nonce was already recorded and has not expired, which means the request
        /// is a replay. An expired record is replaced.
        /// </summary>
        /// <param name="nonceKey">Nonce key (access key and nonce), at most 512 characters.</param>
        /// <param name="expiresUtc">When the record may be forgotten.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the nonce is fresh.</returns>
        /// <exception cref="ArgumentNullException">Thrown when nonceKey is null or empty.</exception>
        Task<bool> TryUseAsync(string nonceKey, DateTime expiresUtc, CancellationToken token = default);

        /// <summary>
        /// Delete expired nonces.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneExpiredAsync(DateTime nowUtc, CancellationToken token = default);
    }
}
