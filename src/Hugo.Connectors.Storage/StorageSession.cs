namespace Hugo.Connectors.Storage
{
    using System;
    using Blobject.Core;

    /// <summary>
    /// A pooled Blobject client for one resource, with its presigner. Owned by the connector session pool; tools must not
    /// dispose it. Thread-safe for concurrent tool calls (Blobject clients are thread-safe).
    /// </summary>
    public class StorageSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Provider.
        /// </summary>
        public StorageProviderEnum Provider { get; }

        /// <summary>
        /// Blobject client. Never null.
        /// </summary>
        public BlobClientBase Client { get; }

        /// <summary>
        /// Presigner, or null when the credentials cannot sign URLs (for example anonymous S3 access).
        /// </summary>
        public IStoragePresigner? Presigner { get; }

        /// <summary>
        /// Reason presigning is unavailable when <see cref="Presigner"/> is null.
        /// </summary>
        public string PresignUnavailableReason { get; }

        /// <summary>
        /// Bucket or container name, for messages and approval summaries.
        /// </summary>
        public string ContainerName { get; }

        /// <summary>
        /// Whether a streamed read downloads the whole object into memory first (true for Google Cloud Storage in
        /// Blobject 6.1.2), so reads of very large objects are refused instead.
        /// </summary>
        public bool BuffersWholeObjectOnRead { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="client">Blobject client.</param>
        /// <param name="presigner">Presigner, or null.</param>
        /// <param name="presignUnavailableReason">Why presigning is unavailable when presigner is null.</param>
        /// <param name="containerName">Bucket or container name.</param>
        /// <param name="buffersWholeObjectOnRead">Whether reads buffer the whole object.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public StorageSession(
            StorageProviderEnum provider,
            BlobClientBase client,
            IStoragePresigner? presigner,
            string? presignUnavailableReason,
            string containerName,
            bool buffersWholeObjectOnRead)
        {
            Provider = provider;
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Presigner = presigner;
            PresignUnavailableReason = presignUnavailableReason ?? String.Empty;
            ContainerName = containerName ?? String.Empty;
            BuffersWholeObjectOnRead = buffersWholeObjectOnRead;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose the client and presigner.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose the client and presigner.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                IDisposable? disposable = Client as IDisposable;
                if (disposable != null) disposable.Dispose();
                if (Presigner != null) Presigner.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
