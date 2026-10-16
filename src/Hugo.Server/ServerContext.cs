namespace Hugo.Server
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using Hugo.Core.Database;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;
    using Hugo.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// Shared dependencies for server modules, plus a typed registry modules use to publish services to each other.
    /// Thread-safe.
    /// </summary>
    public class ServerContext
    {
        #region Public-Members

        /// <summary>
        /// Settings.
        /// </summary>
        public HugoSettings Settings { get; }

        /// <summary>
        /// Logging.
        /// </summary>
        public HugoLog Logging { get; }

        /// <summary>
        /// Database.
        /// </summary>
        public DatabaseDriverBase Database { get; }

        /// <summary>
        /// Secret cipher.
        /// </summary>
        public SecretCipher Cipher { get; }

        /// <summary>
        /// Token service.
        /// </summary>
        public TokenService Tokens { get; }

        /// <summary>
        /// Where the secrets master key came from.
        /// </summary>
        public MasterKeySourceEnum MasterKeySource { get; set; } = MasterKeySourceEnum.Environment;

        /// <summary>
        /// Audit service.
        /// </summary>
        public AuditService Audit { get; }

        /// <summary>
        /// Authentication service.
        /// </summary>
        public AuthenticationService Authentication { get; }

        /// <summary>
        /// Authorization service.
        /// </summary>
        public AuthorizationService Authorization { get; }

        /// <summary>
        /// Telemetry.
        /// </summary>
        public HugoTelemetry Telemetry { get; }

        /// <summary>
        /// Operation scope map.
        /// </summary>
        public OperationScopeMap Scopes { get; }

        /// <summary>
        /// Runs tasks on Hugo's own agent loop for ask_agent_smith (D-012), or null while no module provides it (the
        /// tool is then not listed).
        /// </summary>
        public IAgentDelegationService? AgentDelegation { get; set; } = null;

        /// <summary>
        /// Default tenant identifier seeded on first boot.
        /// </summary>
        public string DefaultTenantId { get; set; } = Hugo.Core.Constants.DefaultTenantId;

        /// <summary>
        /// Node identifier of this server.
        /// </summary>
        public string NodeId
        {
            get
            {
                return Settings.EffectiveNodeId;
            }
        }

        /// <summary>
        /// Cancelled when the replica starts shutting down. Long polls, streams, and synchronous waits watch it and end
        /// early so the replica drains quickly.
        /// </summary>
        public CancellationToken Stopping
        {
            get
            {
                return _Shutdown.Token;
            }
        }


        /// <summary>
        /// Whether the replica is shutting down (readiness then answers 503).
        /// </summary>
        public bool IsDraining
        {
            get
            {
                return _Shutdown.IsCancellationRequested;
            }
        }

        #endregion

        #region Private-Members

        private readonly CancellationTokenSource _Shutdown = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Type, object> _Services = new ConcurrentDictionary<Type, object>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="logging">Logging.</param>
        /// <param name="database">Database.</param>
        /// <param name="cipher">Secret cipher.</param>
        /// <param name="tokens">Token service.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="authentication">Authentication service.</param>
        /// <param name="authorization">Authorization service.</param>
        /// <param name="telemetry">Telemetry.</param>
        /// <param name="scopes">Operation scope map.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ServerContext(HugoSettings settings, HugoLog logging, DatabaseDriverBase database, SecretCipher cipher, TokenService tokens, AuditService audit, AuthenticationService authentication, AuthorizationService authorization, HugoTelemetry telemetry, OperationScopeMap scopes)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            Database = database ?? throw new ArgumentNullException(nameof(database));
            Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            Authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Publish a service for other modules.
        /// </summary>
        /// <typeparam name="T">Service type.</typeparam>
        /// <param name="service">Service.</param>
        /// <exception cref="ArgumentNullException">Thrown when service is null.</exception>
        public void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            _Services[typeof(T)] = service;
        }

        /// <summary>
        /// Begin shutting down: cancel <see cref="Stopping"/>. Safe to call more than once.
        /// </summary>
        public void BeginShutdown()
        {
            try
            {
                _Shutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Resolve a published service.
        /// </summary>
        /// <typeparam name="T">Service type.</typeparam>
        /// <returns>Service.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no module published the service.</exception>
        public T Get<T>() where T : class
        {
            if (_Services.TryGetValue(typeof(T), out object? service)) return (T)service;
            throw new InvalidOperationException("Service " + typeof(T).Name + " has not been registered by any module.");
        }

        /// <summary>
        /// Resolve a published service if present.
        /// </summary>
        /// <typeparam name="T">Service type.</typeparam>
        /// <returns>Service or null.</returns>
        public T? TryGet<T>() where T : class
        {
            if (_Services.TryGetValue(typeof(T), out object? service)) return (T)service;
            return null;
        }

        #endregion
    }
}
