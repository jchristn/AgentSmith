namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Npgsql;

    /// <summary>
    /// Test environment: database providers from HUGO_TEST_PROVIDERS (sqlite by default; for example
    /// sqlite,postgresql), and the PostgreSQL connection from HUGO_TEST_POSTGRES (an Npgsql connection string; default
    /// Host=127.0.0.1;Port=55432;Database=hugo_test;Username=postgres;Password=postgres). Every database a test creates is
    /// isolated: a new Sqlite file or a new PostgreSQL schema.
    /// </summary>
    public static class TestEnvironment
    {
        #region Public-Members

        /// <summary>
        /// Default PostgreSQL connection string for local runs against the test container.
        /// </summary>
        public static readonly string DefaultPostgres = "Host=127.0.0.1;Port=55432;Database=hugo_test;Username=postgres;Password=postgres";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Providers to test.
        /// </summary>
        /// <returns>Providers; never empty.</returns>
        public static List<DatabaseTypeEnum> Providers()
        {
            string raw = Environment.GetEnvironmentVariable("HUGO_TEST_PROVIDERS") ?? Environment.GetEnvironmentVariable("HUGO_TEST_DB") ?? "sqlite";
            List<DatabaseTypeEnum> providers = new List<DatabaseTypeEnum>();
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<DatabaseTypeEnum>(part, true, out DatabaseTypeEnum parsed) && !providers.Contains(parsed)) providers.Add(parsed);
            }
            if (providers.Count == 0) providers.Add(DatabaseTypeEnum.Sqlite);
            return providers;
        }

        /// <summary>
        /// Settings for a new, isolated database.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <returns>Settings.</returns>
        public static DatabaseSettings IsolatedSettings(DatabaseTypeEnum provider)
        {
            DatabaseSettings settings = new DatabaseSettings();
            settings.Type = provider;
            if (provider == DatabaseTypeEnum.Sqlite)
            {
                settings.Filename = Path.Combine(Path.GetTempPath(), "hugo-test", Guid.NewGuid().ToString("N") + ".db");
            }
            else
            {
                string raw = Environment.GetEnvironmentVariable("HUGO_TEST_POSTGRES") ?? DefaultPostgres;
                NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(raw);
                settings.Host = String.IsNullOrEmpty(builder.Host) ? "127.0.0.1" : builder.Host;
                settings.Port = builder.Port;
                settings.DatabaseName = String.IsNullOrEmpty(builder.Database) ? "hugo_test" : builder.Database;
                settings.Username = String.IsNullOrEmpty(builder.Username) ? "postgres" : builder.Username;
                settings.Password = builder.Password;
                settings.Schema = "t_" + Guid.NewGuid().ToString("N").Substring(0, 16);
                settings.MaxPoolSize = 20;
            }
            return settings;
        }

        /// <summary>
        /// Create and initialize a driver on an isolated database.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Driver.</returns>
        public static async Task<DatabaseDriverBase> CreateDriverAsync(DatabaseTypeEnum provider, CancellationToken token = default)
        {
            return await DatabaseDriverFactory.CreateAndInitializeAsync(IsolatedSettings(provider), token).ConfigureAwait(false);
        }

        #endregion
    }
}
