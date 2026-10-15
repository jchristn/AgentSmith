namespace Hugo.Connectors.Storage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Storage.Blobs;
    using Azure.Storage.Sas;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Creates Azure Blob service SAS URLs with the account key. Thread-safe.
    /// </summary>
    public class AzureBlobPresigner : IStoragePresigner
    {
        #region Private-Members

        private readonly BlobContainerClient _Container;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a container client that was built with a shared key (account key or a connection string
        /// containing one).
        /// </summary>
        /// <param name="container">Container client.</param>
        /// <exception cref="ArgumentNullException">Thrown when container is null.</exception>
        public AzureBlobPresigner(BlobContainerClient container)
        {
            _Container = container ?? throw new ArgumentNullException(nameof(container));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<string> PresignAsync(string key, StoragePresignMethodEnum method, TimeSpan expiry, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            token.ThrowIfCancellationRequested();
            BlobClient blob = _Container.GetBlobClient(key);
            if (!blob.CanGenerateSasUri)
                throw new ConnectorException("not_supported", "This Azure resource's credentials cannot sign SAS URLs; bind an account key or a connection string that includes AccountKey.");
            BlobSasPermissions permissions = method == StoragePresignMethodEnum.Put
                ? BlobSasPermissions.Create | BlobSasPermissions.Write
                : BlobSasPermissions.Read;
            Uri uri = blob.GenerateSasUri(permissions, DateTimeOffset.UtcNow.Add(expiry));
            return Task.FromResult(uri.ToString());
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            _Disposed = true;
        }

        #endregion
    }
}
