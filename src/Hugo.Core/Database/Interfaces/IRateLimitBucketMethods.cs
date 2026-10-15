namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Fixed-window rate limit counters shared by every control-plane replica (P10.3).
    /// </summary>
    public interface IRateLimitBucketMethods
    {
        /// <summary>
        /// Add one hit to a bucket, creating it when missing, and return the bucket's hits including this one.
        /// </summary>
        /// <param name="bucketKey">Bucket key (scope, caller, and window), at most 512 characters.</param>
        /// <param name="expiresUtc">When the bucket may be deleted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Hits in the bucket.</returns>
        /// <exception cref="ArgumentNullException">Thrown when bucketKey is null or empty.</exception>
        Task<int> IncrementAsync(string bucketKey, DateTime expiresUtc, CancellationToken token = default);

        /// <summary>
        /// Delete expired buckets.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneExpiredAsync(DateTime nowUtc, CancellationToken token = default);
    }
}
