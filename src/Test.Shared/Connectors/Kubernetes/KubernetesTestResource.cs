namespace Test.Shared.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A Kubernetes resource for tests: settings, secrets, and a private session pool. Dispose it to close pooled
    /// sessions and clear secrets.
    /// </summary>
    public class KubernetesTestResource : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Resource context handed to the connector.
        /// </summary>
        public ConnectorResourceContext Context { get; }

        /// <summary>
        /// Session pool used by the resource.
        /// </summary>
        public NoPoolSessionPool Pool { get; } = new NoPoolSessionPool();

        #endregion

        #region Private-Members

        private readonly SecretAccessor _Secrets;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Resource name.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="secrets">Secrets by slot and field, or null.</param>
        public KubernetesTestResource(string name, Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets)
        {
            _Secrets = new SecretAccessor(secrets);
            Context = new ConnectorResourceContext
            {
                ResourceId = "res_" + name,
                TenantId = "ten_test",
                Name = name,
                ConnectorType = "kubernetes",
                Settings = new ResourceSettings(settings),
                Secrets = _Secrets,
                ConfigurationHash = Guid.NewGuid().ToString("N"),
                Sessions = Pool
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="accessMode">Access mode.</param>
        /// <param name="maxBytes">Output byte limit.</param>
        /// <param name="maxLines">Output line limit.</param>
        /// <param name="timeoutSeconds">Call timeout.</param>
        /// <returns>Context.</returns>
        public ConnectorInvocationContext Invocation(AccessModeEnum accessMode = AccessModeEnum.ReadWrite, int maxBytes = 65536, int maxLines = 1000, int timeoutSeconds = 120)
        {
            return new ConnectorInvocationContext
            {
                Resource = Context,
                BindingAlias = Context.Name,
                AccessMode = accessMode,
                Limits = new OutputLimits { MaxBytes = maxBytes, MaxLines = maxLines, MaxRows = 500 },
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
                Pool.Dispose();
                _Secrets.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
