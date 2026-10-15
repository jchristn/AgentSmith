namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;

    /// <summary>
    /// Provider-neutral fixed-window rate limit counters: one atomic upsert per hit. Thread-safe.
    /// </summary>
    public class AdoRateLimitBucketMethods : IRateLimitBucketMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoRateLimitBucketMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("rate_limit_buckets");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<int> IncrementAsync(string bucketKey, DateTime expiresUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(bucketKey)) throw new ArgumentNullException(nameof(bucketKey));
            string key = bucketKey.Length > 512 ? bucketKey.Substring(0, 512) : bucketKey;
            int hits = 0;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                List<int> rows = await AdoDatabaseDriver.QueryAsync(connection, transaction,
                    "INSERT INTO " + _Table + " AS b (bucket_key, hits, expires_utc) VALUES (@key, 1, @expires) " +
                    "ON CONFLICT (bucket_key) DO UPDATE SET hits = b.hits + 1 RETURNING hits;",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "key", key);
                        AdoDatabaseDriver.Add(command, "expires", expiresUtc);
                    },
                    reader => AdoConverters.Int(reader, "hits"),
                    ct).ConfigureAwait(false);
                hits = rows.Count > 0 ? rows[0] : 1;
            }, token).ConfigureAwait(false);
            return hits;
        }

        /// <inheritdoc />
        public async Task<int> PruneExpiredAsync(DateTime nowUtc, CancellationToken token = default)
        {
            return await _Driver.NonQueryAsync(
                "DELETE FROM " + _Table + " WHERE expires_utc < @now;",
                command => AdoDatabaseDriver.Add(command, "now", nowUtc),
                token).ConfigureAwait(false);
        }

        #endregion
    }
}
