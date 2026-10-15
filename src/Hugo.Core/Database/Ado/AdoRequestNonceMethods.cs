namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;

    /// <summary>
    /// Provider-neutral signed-request nonce store. One upsert per signed request; a fresh nonce inserts a row, a replay
    /// inside its window changes nothing. Thread-safe.
    /// </summary>
    public class AdoRequestNonceMethods : IRequestNonceMethods
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
        public AdoRequestNonceMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("request_nonces");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> TryUseAsync(string nonceKey, DateTime expiresUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(nonceKey)) throw new ArgumentNullException(nameof(nonceKey));
            string key = nonceKey.Length > 512 ? nonceKey.Substring(0, 512) : nonceKey;
            DateTime now = DateTime.UtcNow;

            // Insert, or take over an expired row; a live row makes the statement affect nothing (a replay).
            int affected = await _Driver.NonQueryAsync(
                "INSERT INTO " + _Table + " AS n (nonce_key, expires_utc, created_utc) VALUES (@key, @expires, @now) " +
                "ON CONFLICT (nonce_key) DO UPDATE SET expires_utc = @expires, created_utc = @now WHERE n.expires_utc < @now;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "key", key);
                    AdoDatabaseDriver.Add(command, "expires", expiresUtc);
                    AdoDatabaseDriver.Add(command, "now", now);
                },
                token).ConfigureAwait(false);
            return affected == 1;
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
