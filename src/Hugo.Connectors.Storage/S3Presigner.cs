namespace Hugo.Connectors.Storage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Blobject.AmazonS3;

    /// <summary>
    /// Presigns S3 URLs (SigV4) with the AWS SDK. Uses path-style addressing when a custom endpoint is configured, the
    /// same rule Blobject applies. Thread-safe.
    /// </summary>
    public class S3Presigner : IStoragePresigner
    {
        #region Private-Members

        private readonly AmazonS3Client _Client;
        private readonly string _Bucket;
        private readonly bool _Ssl;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate from the Blobject settings used for the same resource.
        /// </summary>
        /// <param name="settings">S3 settings with credentials.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the settings carry no credentials.</exception>
        public S3Presigner(AwsSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (!settings.HasCredentials) throw new ArgumentException("Presigning requires an access key pair.", nameof(settings));
            _Bucket = settings.Bucket;
            _Ssl = settings.Ssl;

            AmazonS3Config config = new AmazonS3Config();
            if (String.IsNullOrEmpty(settings.Endpoint))
            {
                config.RegionEndpoint = settings.AwsRegion;
            }
            else
            {
                config.ServiceURL = settings.Endpoint;
                config.ForcePathStyle = true;
                config.AuthenticationRegion = settings.Region;
            }
            config.UseHttp = !settings.Ssl;
            _Client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string> PresignAsync(string key, StoragePresignMethodEnum method, TimeSpan expiry, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            token.ThrowIfCancellationRequested();
            GetPreSignedUrlRequest request = new GetPreSignedUrlRequest
            {
                BucketName = _Bucket,
                Key = key,
                Expires = DateTime.UtcNow.Add(expiry),
                Verb = method == StoragePresignMethodEnum.Put ? HttpVerb.PUT : HttpVerb.GET,
                Protocol = _Ssl ? Protocol.HTTPS : Protocol.HTTP
            };
            return await _Client.GetPreSignedURLAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose the SDK client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose the SDK client.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Client.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
