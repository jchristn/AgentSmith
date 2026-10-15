namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Security;

    /// <summary>
    /// A resolved resource context for one connector call (P3.8): settings with defaults, decrypted secrets that live
    /// only until this lease is disposed (the accessor is zeroed then), a scrubber that knows those secret values, and
    /// the settings the connector learned during the call. Dispose it as soon as the call ends. Thread-safe.
    /// </summary>
    public class ConnectorContextLease : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The context handed to the connector.
        /// </summary>
        public ConnectorResourceContext Context { get; }

        /// <summary>
        /// Scrubber with the call's secret values (plain, base64, and URL-encoded forms) plus the secret patterns.
        /// </summary>
        public SecretScrubber Scrubber { get; }

        /// <summary>
        /// Settings the connector learned (key to value), in the order learned.
        /// </summary>
        public ConcurrentDictionary<string, string> LearnedSettings { get; } = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Writes started by LearnSetting; await them before reporting the call finished.
        /// </summary>
        public ConcurrentBag<Task> PendingWrites { get; } = new ConcurrentBag<Task>();

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="scrubber">Scrubber.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ConnectorContextLease(ConnectorResourceContext context, SecretScrubber scrubber)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wait for learned-setting writes to finish. Failures are swallowed (they are logged where they happen).
        /// </summary>
        /// <returns>Task.</returns>
        public async Task FlushAsync()
        {
            List<Task> tasks = new List<Task>(PendingWrites);
            foreach (Task task in tasks)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// Zero the secrets.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Zero the secrets and forget the known values.
        /// </summary>
        /// <param name="disposing">Whether called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Context.Secrets.Dispose();
                Scrubber.ClearKnownValues();
            }
            _Disposed = true;
        }

        #endregion
    }
}
