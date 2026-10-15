namespace Hugo.Core.Database.Postgresql
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Ado;
    using Hugo.Core.Database.Postgresql.Queries;
    using Hugo.Core.Enums;
    using Npgsql;

    /// <summary>
    /// PostgreSQL driver. Thread-safe.
    /// </summary>
    public class PostgresqlDatabaseDriver : AdoDatabaseDriver
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum DatabaseType
        {
            get
            {
                return DatabaseTypeEnum.Postgresql;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public PostgresqlDatabaseDriver(DatabaseSettings settings)
            : base(settings, new PostgresqlDialect())
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override DbConnection CreateConnection()
        {
            return new NpgsqlConnection(Settings.ToPostgresqlConnectionString());
        }

        /// <inheritdoc />
        public override async Task<DatabaseLockHandle> AcquireStartupLockAsync(CancellationToken token = default)
        {
            long key = LockKey(StartupLockName());
            NpgsqlConnection connection = new NpgsqlConnection(Settings.ToPostgresqlListenerConnectionString());
            try
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand command = new NpgsqlCommand("SELECT pg_advisory_lock(@key);", connection))
                {
                    // Waiting for another replica's migrations can take longer than any command timeout.
                    command.CommandTimeout = 0;
                    command.Parameters.AddWithValue("key", key);
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return new DatabaseLockHandle(async () =>
            {
                try
                {
                    using (NpgsqlCommand unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key);", connection))
                    {
                        unlock.Parameters.AddWithValue("key", key);
                        await unlock.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception e) when (e is NpgsqlException || e is InvalidOperationException)
                {
                    // Closing the session below releases the lock anyway.
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            });
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose: also empties this driver's Npgsql connection pools, so idle connections are closed now instead of
        /// lingering for <see cref="DatabaseSettings.ConnectionIdleLifetimeSeconds"/>. Each distinct connection string
        /// has its own pool, so processes that create many short-lived drivers (tests, tools) would otherwise
        /// accumulate idle server connections.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearPool(Settings.ToPostgresqlConnectionString());
                ClearPool(Settings.ToPostgresqlListenerConnectionString());
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Private-Methods

        private static void ClearPool(string connectionString)
        {
            try
            {
                using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
                {
                    NpgsqlConnection.ClearPool(connection);
                }
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
            }
        }

        #endregion
    }
}
