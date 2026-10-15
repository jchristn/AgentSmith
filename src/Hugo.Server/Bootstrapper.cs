namespace Hugo.Server
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;
    using Hugo.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// Composition root (P1.3): settings, logging, database, master key, seeding, services, server, and graceful
    /// shutdown on SIGTERM and Ctrl+C.
    /// </summary>
    public static class Bootstrapper
    {
        #region Public-Methods

        /// <summary>
        /// Run the server until shutdown is signaled. Returns the process exit code (75 after a restart request, so a
        /// container restart policy brings the server back).
        /// </summary>
        /// <param name="args">Command-line arguments; the first may be the settings file path.</param>
        /// <returns>Exit code.</returns>
        public static int Run(string[] args)
        {
            CancellationTokenSource shutdown = new CancellationTokenSource();
            Action signal = () =>
            {
                try
                {
                    shutdown.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            };
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                signal();
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => signal();
            RestartCoordinator.Requested += (sender, e) => signal();
            using (PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                signal();
            }))
            {
                return RunAsync(args, shutdown.Token).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Build the master key options from HUGO_MASTER_KEY, HUGO_MASTER_KEY_PREVIOUS, and the settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static MasterKeyOptions MasterKeyOptionsFrom(HugoSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            MasterKeyOptions options = new MasterKeyOptions();
            options.EnvironmentKey = Environment.GetEnvironmentVariable("HUGO_MASTER_KEY");
            options.EnvironmentPreviousKey = Environment.GetEnvironmentVariable("HUGO_MASTER_KEY_PREVIOUS");
            options.SettingsKey = settings.Secrets.MasterKey;
            options.KeyFile = settings.Secrets.MasterKeyFile;
            options.KeyVersion = settings.Secrets.MasterKeyVersion;
            options.PreviousKeyVersion = settings.Secrets.PreviousMasterKeyVersion;
            return options;
        }

        /// <summary>
        /// Build and start a server, returning it without blocking. Used by tests and by <see cref="Run"/>.
        /// </summary>
        /// <param name="settings">Settings, already loaded and overridden.</param>
        /// <param name="logging">Logging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="keyOptions">Master key options; null reads them from the environment and settings.</param>
        /// <param name="additionalConnectors">Connectors registered next to the built-ins (tests and embedding hosts), or null.</param>
        /// <returns>Started server.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="MasterKeyException">Thrown when the master key is missing or does not match the stored canary.</exception>
        public static async Task<HugoServer> StartAsync(HugoSettings settings, LoggingModule logging, CancellationToken token = default, MasterKeyOptions? keyOptions = null, System.Collections.Generic.IEnumerable<Hugo.Connectors.Abstractions.IConnector>? additionalConnectors = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            settings.Validate();

            DatabaseDriverBase database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings.Database, token).ConfigureAwait(false);
            try
            {
                if (database.MigrationsApplied > 0) logging.Info("[Hugo] applied " + database.MigrationsApplied + " schema migrations");
                if (settings.Database.Type == DatabaseTypeEnum.Sqlite) logging.Info("[Hugo] database initialized (Sqlite at " + settings.Database.Filename + ", schema version " + database.SchemaVersion + ")");
                else logging.Info("[Hugo] database initialized (Postgresql at " + settings.Database.Host + ":" + settings.Database.Port + "/" + settings.Database.DatabaseName + ", schema version " + database.SchemaVersion + ")");

                MasterKeyLoadResult key = MasterKeyLoader.Load(keyOptions ?? MasterKeyOptionsFrom(settings), message => logging.Alert("[Hugo] " + message));
                SecretCipher cipher = key.Cipher;
                await MasterKeyGuard.VerifyAsync(database, cipher, message => logging.Info("[Hugo] " + message), token).ConfigureAwait(false);
                logging.Info("[Hugo] secrets master key version " + cipher.KeyVersion + " loaded from " + key.Source + (key.HasPreviousKey ? " (previous key configured)" : String.Empty));
                if (settings.Auth.SigningKey == new AuthSettings().SigningKey) logging.Warn("[Hugo] Auth.SigningKey is the built-in default. Set HUGO_AUTH_SIGNING_KEY.");

                SeedOptions seed = new SeedOptions();
                seed.PasswordHashIterations = settings.Auth.PasswordHashIterations;
                Tenant defaultTenant = await new DefaultSeeder(database, message => logging.Info("[Hugo] " + message)).SeedAsync(seed, token).ConfigureAwait(false);

                AuditService audit = new AuditService(database, message => logging.Warn("[Hugo] " + message));
                TokenService tokens = new TokenService(settings.Auth.SigningKey, settings.Auth.Issuer, settings.Auth.UserSessionMinutes);
                AuthenticationService authentication = new AuthenticationService(database, tokens, cipher, settings, audit);
                AuthorizationService authorization = new AuthorizationService(database, audit);
                HugoTelemetry telemetry = new HugoTelemetry(settings.Telemetry, settings.EffectiveNodeId, message =>
                {
                    if (message.StartsWith("telemetry", StringComparison.Ordinal)) logging.Warn("[Hugo.Telemetry] " + message);
                    else logging.Debug("[Hugo.Telemetry] " + message);
                });

                ServerContext context = new ServerContext(settings, logging, database, cipher, tokens, audit, authentication, authorization, telemetry, new OperationScopeMap());
                context.DefaultTenantId = defaultTenant.Id;
                context.MasterKeySource = key.Source;

                HugoServer server = new HugoServer(context);
                if (additionalConnectors != null)
                {
                    foreach (Hugo.Server.Modules.IntegrationModule module in System.Linq.Enumerable.OfType<Hugo.Server.Modules.IntegrationModule>(server.Modules)) module.AdditionalConnectors.AddRange(additionalConnectors);
                }
                await server.StartAsync(token).ConfigureAwait(false);
                return server;
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Build logging from settings.
        /// </summary>
        /// <param name="settings">Logging settings.</param>
        /// <returns>Logging module.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static LoggingModule BuildLogging(Hugo.Server.Settings.LoggingSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            LoggingModule logging = new LoggingModule();
            // Skip SyslogLogging's terminal probe: containers have no terminal, but their stdout is still collected.
            logging.Settings.DetectConsole = false;
            logging.Settings.EnableConsole = settings.ConsoleLogging;
            logging.Settings.MinimumSeverity = (Severity)settings.MinimumSeverity;
            if (settings.FileLogging)
            {
                logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
                logging.Settings.LogFilename = settings.LogDirectory + settings.LogFilename;
            }
            return logging;
        }

        #endregion

        #region Private-Methods

        private static async Task<int> RunAsync(string[] args, CancellationToken shutdown)
        {
            string settingsFile = ResolveSettingsFile(args);
            HugoSettings settings;
            try
            {
                settings = HugoSettings.FromFile(settingsFile);
                if (!settings.SettingsFileReadOnly && Environment.GetEnvironmentVariable("HUGO_SETTINGS_READONLY") != "true")
                {
                    try
                    {
                        settings.ToFile(settingsFile);
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException || e is System.IO.IOException)
                    {
                        settings.SettingsFileReadOnly = true;
                    }
                }
                settings.ApplyEnvironment();
                settings.Validate();
            }
            catch (Exception e) when (e is System.IO.InvalidDataException || e is InvalidOperationException || e is ArgumentException)
            {
                Console.Error.WriteLine("[Hugo] invalid settings: " + e.Message);
                return 1;
            }

            LoggingModule logging = BuildLogging(settings.Logging);
            logging.Info("[Hugo] " + Constants.ProductName + " " + Constants.ProductVersion + " starting on node " + settings.EffectiveNodeId + " with settings " + settingsFile);

            HugoServer server;
            try
            {
                server = await StartAsync(settings, logging, shutdown).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logging.Alert("[Hugo] startup failed: " + SecretScrubber.Shared.Scrub(e.Message));
                return 1;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, shutdown).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            logging.Info("[Hugo] stopping");
            using (CancellationTokenSource stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                await server.StopAsync(stopTimeout.Token).ConfigureAwait(false);
            }
            server.Dispose();
            server.Context.Telemetry.Dispose();
            server.Context.Database.Dispose();
            return RestartCoordinator.ExitCode;
        }

        private static string ResolveSettingsFile(string[] args)
        {
            string? fromEnv = Environment.GetEnvironmentVariable("HUGO_SETTINGS_FILE");
            if (!String.IsNullOrEmpty(fromEnv)) return fromEnv;
            if (args != null && args.Length > 0 && !String.IsNullOrEmpty(args[0])) return args[0];
            if (System.IO.File.Exists("hugo.json")) return "hugo.json";

            // Without a settings file in the working directory (for example dotnet run --project from the repository
            // root), use the hugo.json copied next to the executable and keep relative paths (data/, logs/) there too.
            string beside = System.IO.Path.Combine(AppContext.BaseDirectory, "hugo.json");
            if (System.IO.File.Exists(beside))
            {
                Environment.CurrentDirectory = AppContext.BaseDirectory;
                return beside;
            }
            return "hugo.json";
        }

        #endregion
    }
}
