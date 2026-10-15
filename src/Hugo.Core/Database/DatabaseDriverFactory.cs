namespace Hugo.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Postgresql;
    using Hugo.Core.Database.Sqlite;
    using Hugo.Core.Enums;

    /// <summary>
    /// Composition root for the data layer.
    /// </summary>
    public static class DatabaseDriverFactory
    {
        /// <summary>
        /// Create a driver for the configured provider.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <returns>Driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the provider is unsupported.</exception>
        public static DatabaseDriverBase Create(DatabaseSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteDatabaseDriver(settings);
                case DatabaseTypeEnum.Postgresql:
                    return new PostgresqlDatabaseDriver(settings);
                default:
                    throw new ArgumentException("Unsupported database type: " + settings.Type.ToString(), nameof(settings));
            }
        }

        /// <summary>
        /// Create and initialize a driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Initialized driver.</returns>
        public static async Task<DatabaseDriverBase> CreateAndInitializeAsync(DatabaseSettings settings, CancellationToken token = default)
        {
            DatabaseDriverBase driver = Create(settings);
            await driver.InitializeAsync(token).ConfigureAwait(false);
            return driver;
        }
    }
}
