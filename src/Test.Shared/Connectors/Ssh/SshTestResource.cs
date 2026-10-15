namespace Test.Shared.Connectors.Ssh
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// An SSH resource for tests: settings, secrets, a private session pool, and a record of learned settings
    /// (trust-on-first-use fingerprints). Dispose it to close pooled sessions.
    /// </summary>
    public class SshTestResource : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Resource context handed to the connector.
        /// </summary>
        public ConnectorResourceContext Context { get; }

        /// <summary>
        /// Settings the connector learned and asked to persist.
        /// </summary>
        public ConcurrentDictionary<string, string> Learned { get; } = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Session pool used by the resource.
        /// </summary>
        public IConnectorSessionPool Pool { get; }

        #endregion

        #region Private-Members

        private readonly SecretAccessor _Secrets;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Resource name (also the pool key, so keep it unique per configuration).</param>
        /// <param name="settings">Settings.</param>
        /// <param name="secrets">Secrets by slot and field.</param>
        /// <param name="pool">Session pool, or null for a new <see cref="NoPoolSessionPool"/>.</param>
        public SshTestResource(string name, Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets, IConnectorSessionPool? pool = null)
        {
            _Secrets = new SecretAccessor(secrets);
            Pool = pool ?? new NoPoolSessionPool();
            Context = new ConnectorResourceContext
            {
                ResourceId = "res_" + name,
                TenantId = "ten_test",
                Name = name,
                ConnectorType = "ssh",
                Settings = new ResourceSettings(settings),
                Secrets = _Secrets,
                ConfigurationHash = Guid.NewGuid().ToString("N"),
                Sessions = Pool,
                LearnSetting = (key, value) => Learned[key] = value
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="maxBytes">Output byte limit.</param>
        /// <param name="maxLines">Output line limit.</param>
        /// <param name="maxRows">Row limit.</param>
        /// <param name="timeoutSeconds">Call timeout.</param>
        /// <param name="accessMode">Access mode.</param>
        /// <returns>Context.</returns>
        public ConnectorInvocationContext Invocation(int maxBytes = 65536, int maxLines = 1000, int maxRows = 500, int timeoutSeconds = 120, AccessModeEnum accessMode = AccessModeEnum.ReadWrite)
        {
            return new ConnectorInvocationContext
            {
                Resource = Context,
                BindingAlias = Context.Name,
                AccessMode = accessMode,
                Limits = new OutputLimits { MaxBytes = maxBytes, MaxLines = maxLines, MaxRows = maxRows },
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                PrincipalSummary = "test",
                CorrelationId = Guid.NewGuid().ToString("N")
            };
        }

        /// <summary>
        /// Close pooled sessions and clear secrets.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Close pooled sessions and clear secrets.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                if (Pool is IDisposable d) d.Dispose();
                _Secrets.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
