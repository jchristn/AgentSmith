namespace Hugo.Connectors.Storage
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Google.Apis.Auth.OAuth2;
    using Google.Cloud.Storage.V1;

    /// <summary>
    /// Creates V4 signed URLs for Google Cloud Storage with the service account key. When a custom endpoint is
    /// configured the URL points at it (scheme, host, and port). Thread-safe.
    /// </summary>
    public class GcsPresigner : IStoragePresigner
    {
        #region Private-Members

        private readonly UrlSigner _Signer;
        private readonly string _Bucket;
        private readonly Uri? _Endpoint;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="serviceAccountJson">Service account key JSON.</param>
        /// <param name="bucket">Bucket.</param>
        /// <param name="customEndpoint">Optional custom endpoint (emulators); null for Google Cloud Storage.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public GcsPresigner(string serviceAccountJson, string bucket, Uri? customEndpoint)
        {
            if (String.IsNullOrEmpty(serviceAccountJson)) throw new ArgumentNullException(nameof(serviceAccountJson));
            if (String.IsNullOrEmpty(bucket)) throw new ArgumentNullException(nameof(bucket));
            ServiceAccountCredential credential = CredentialFactory.FromJson<ServiceAccountCredential>(serviceAccountJson);
            _Signer = UrlSigner.FromCredential(credential);
            _Bucket = bucket;
            _Endpoint = customEndpoint;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string> PresignAsync(string key, StoragePresignMethodEnum method, TimeSpan expiry, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            UrlSigner.RequestTemplate template = UrlSigner.RequestTemplate
                .FromBucket(_Bucket)
                .WithObjectName(key)
                .WithHttpMethod(method == StoragePresignMethodEnum.Put ? HttpMethod.Put : HttpMethod.Get);
            UrlSigner.Options options = UrlSigner.Options.FromDuration(expiry);
            if (_Endpoint != null)
            {
                options = options.WithScheme(_Endpoint.Scheme).WithHost(_Endpoint.Host);
                if (!_Endpoint.IsDefaultPort) options = options.WithPort(_Endpoint.Port);
            }
            return await _Signer.SignAsync(template, options, token).ConfigureAwait(false);
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
