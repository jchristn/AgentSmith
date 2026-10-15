namespace Hugo.Connectors.Docker
{
    using System;
    using global::Docker.DotNet;

    /// <summary>
    /// A pooled connection to one Docker daemon. Thread-safe (the underlying client is).
    /// </summary>
    public class DockerSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The client. Do not dispose; the session owns it.
        /// </summary>
        public DockerClient Client { get; }

        /// <summary>
        /// Endpoint label for messages, for example <c>tcp://10.0.0.5:2376</c>.
        /// </summary>
        public string Endpoint { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="client">Client; the session takes ownership.</param>
        /// <param name="endpoint">Endpoint label.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public DockerSession(DockerClient client, string endpoint)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Endpoint = endpoint ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose the client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose the client.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) Client.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
