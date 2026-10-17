namespace Hugo.Connectors.Storage
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Storage;
    using Azure.Storage.Blobs;
    using Blobject.AmazonS3;
    using Blobject.AzureBlob;
    using Blobject.Core;
    using Blobject.GoogleCloud;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds Blobject clients (and presigners) from a resolved resource. Construction performs no network I/O.
    /// Thread-safe.
    /// </summary>
    public static class StorageClientFactory
    {
        #region Public-Members

        /// <summary>
        /// Session pool key suffix for storage sessions.
        /// </summary>
        public const string SessionSuffix = "blobject";

        /// <summary>
        /// Default S3 region when none is set.
        /// </summary>
        public const string DefaultRegion = "us-east-1";

        /// <summary>
        /// Retries the Azure SDK makes for a failed request (connection refused, 5xx, throttling). Minimum 0, maximum 10,
        /// default 2. The SDK default (3 retries with long backoff) can outlast a tool call's timeout.
        /// </summary>
        public static int AzureMaxRetries
        {
            get
            {
                return _AzureMaxRetries;
            }
            set
            {
                _AzureMaxRetries = Math.Clamp(value, 0, 10);
            }
        }

        #endregion

        #region Private-Members

        private static int _AzureMaxRetries = 2;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Azure SDK client options used for every Azure session: bounded exponential retries (see
        /// <see cref="AzureMaxRetries"/>), 0.5 second initial delay, 4 second maximum delay, 60 second network timeout.
        /// </summary>
        /// <returns>Options.</returns>
        public static BlobClientOptions BuildAzureClientOptions()
        {
            BlobClientOptions options = new BlobClientOptions();
            options.Retry.MaxRetries = _AzureMaxRetries;
            options.Retry.Mode = Azure.Core.RetryMode.Exponential;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            options.Retry.MaxDelay = TimeSpan.FromSeconds(4);
            options.Retry.NetworkTimeout = TimeSpan.FromSeconds(60);
            return options;
        }

        /// <summary>
        /// Get the pooled session for a resource, creating it when needed.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="resource">Resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session. Callers must not dispose it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when settings or credentials are invalid.</exception>
        public static Task<StorageSession> GetSessionAsync(StorageProviderEnum provider, ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.Sessions.GetOrCreateAsync<StorageSession>(
                resource.SessionKey(SessionSuffix),
                ct => Task.FromResult(CreateSession(provider, resource)),
                token);
        }

        /// <summary>
        /// Drop the pooled session for a resource (for example after an authentication failure).
        /// </summary>
        /// <param name="resource">Resource.</param>
        public static void InvalidateSession(ConnectorResourceContext resource)
        {
            if (resource == null) return;
            resource.Sessions.Invalidate(resource.SessionKey(SessionSuffix));
        }

        /// <summary>
        /// Create a new, unpooled session. The caller owns and must dispose it.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="resource">Resource.</param>
        /// <returns>Session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Code <c>invalid_setting</c> or <c>invalid_credentials</c> when the resource
        /// cannot be turned into a client.</exception>
        public static StorageSession CreateSession(StorageProviderEnum provider, ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            switch (provider)
            {
                case StorageProviderEnum.AmazonS3:
                    return CreateS3Session(resource);
                case StorageProviderEnum.AzureBlob:
                    return CreateAzureSession(resource);
                case StorageProviderEnum.GoogleCloud:
                    return CreateGcsSession(resource);
                default:
                    throw new ConnectorException("invalid_setting", "Unknown storage provider " + provider + ".");
            }
        }

        /// <summary>
        /// Build Blobject S3 settings from a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when a setting or the access key pair is invalid.</exception>
        public static AwsSettings BuildS3Settings(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ResourceSettings settings = resource.Settings;
            AwsSettings aws = new AwsSettings();
            aws.Bucket = settings.GetRequiredString(StorageSettingKeys.Bucket);
            aws.Region = settings.GetString(StorageSettingKeys.Region, DefaultRegion) ?? DefaultRegion;
            bool useSsl = settings.GetBool(StorageSettingKeys.UseSsl, true);
            Uri? endpoint = ParseEndpoint(settings.GetString(StorageSettingKeys.Endpoint), useSsl, StorageSettingKeys.Endpoint);
            if (endpoint != null)
            {
                aws.Endpoint = endpoint.GetLeftPart(UriPartial.Authority);
                aws.Ssl = String.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                aws.Ssl = useSsl;
            }

            if (resource.Secrets.HasSlot(StorageSettingKeys.SlotAccessKeyPair))
            {
                string? accessKeyId = resource.Secrets.Get(StorageSettingKeys.SlotAccessKeyPair, StorageSettingKeys.FieldAccessKeyId);
                string? secretAccessKey = resource.Secrets.Get(StorageSettingKeys.SlotAccessKeyPair, StorageSettingKeys.FieldSecretAccessKey);
                accessKeyId = S3CredentialNormalizer.Normalize(accessKeyId, endpoint == null);
                secretAccessKey = S3CredentialNormalizer.Normalize(secretAccessKey, endpoint == null);
                if (String.IsNullOrEmpty(accessKeyId) || String.IsNullOrEmpty(secretAccessKey))
                    throw new ConnectorException("invalid_credentials", "The accessKeyPair secret must have both accessKeyId and secretAccessKey.");
                if (!String.IsNullOrEmpty(resource.Secrets.Get(StorageSettingKeys.SlotAccessKeyPair, StorageSettingKeys.FieldSessionToken)))
                    throw new ConnectorException("invalid_credentials", "Temporary credentials (sessionToken) are not supported by the Blobject S3 client; bind a long-lived access key pair.");
                if (endpoint == null && (accessKeyId.Any(Char.IsWhiteSpace) || secretAccessKey.Any(Char.IsWhiteSpace)))
                    throw new ConnectorException("invalid_credentials", "AWS access keys must not contain whitespace. Replace the bound secret values without spaces or line breaks; Hugo preserves secret values exactly.");
                aws.AccessKey = accessKeyId;
                aws.SecretKey = secretAccessKey;
            }
            return aws;
        }

        /// <summary>
        /// Resolve the Azure blob endpoint: the configured one, or <c>https://{accountName}.blob.core.windows.net</c>.
        /// </summary>
        /// <param name="settings">Resource settings.</param>
        /// <returns>Endpoint without a trailing slash, or null when neither endpoint nor account name is set.</returns>
        /// <exception cref="ConnectorException">Thrown when the endpoint is not an absolute HTTP or HTTPS URL.</exception>
        public static string? ResolveAzureEndpoint(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Uri? endpoint = ParseEndpoint(settings.GetString(StorageSettingKeys.Endpoint), true, StorageSettingKeys.Endpoint);
            if (endpoint != null) return endpoint.ToString().TrimEnd('/');
            string? account = settings.GetString(StorageSettingKeys.AccountName);
            if (String.IsNullOrEmpty(account)) return null;
            return "https://" + account + ".blob.core.windows.net";
        }

        /// <summary>
        /// Normalize a GCS custom endpoint to the JSON API base URI Blobject expects
        /// (<c>scheme://host:port/storage/v1/</c>).
        /// </summary>
        /// <param name="value">Configured value; null or empty means Google Cloud Storage itself.</param>
        /// <returns>Normalized base URI, or null.</returns>
        /// <exception cref="ConnectorException">Thrown when the value is not an absolute HTTP or HTTPS URL.</exception>
        public static string? NormalizeGcsEndpoint(string? value)
        {
            Uri? endpoint = ParseEndpoint(value, true, StorageSettingKeys.CustomEndpoint);
            if (endpoint == null) return null;
            string text = endpoint.ToString().TrimEnd('/');
            if (!text.EndsWith("/storage/v1", StringComparison.OrdinalIgnoreCase)) text += "/storage/v1";
            return text + "/";
        }

        /// <summary>
        /// Parse an endpoint setting. A value without a scheme gets <c>https://</c> or <c>http://</c> from
        /// <paramref name="defaultSsl"/>.
        /// </summary>
        /// <param name="value">Value; null or empty returns null.</param>
        /// <param name="defaultSsl">Scheme to assume when none is given.</param>
        /// <param name="settingKey">Setting key used in messages.</param>
        /// <returns>Absolute URI, or null.</returns>
        /// <exception cref="ConnectorException">Code <c>invalid_setting</c> when the value is not an HTTP or HTTPS URL.</exception>
        public static Uri? ParseEndpoint(string? value, bool defaultSsl, string settingKey)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            if (!text.Contains("://", StringComparison.Ordinal)) text = (defaultSsl ? "https://" : "http://") + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || String.IsNullOrEmpty(uri.Host))
                throw new ConnectorException("invalid_setting", "Setting '" + settingKey + "' must be an absolute http or https URL.");
            return uri;
        }

        #endregion

        #region Private-Methods

        private static StorageSession CreateS3Session(ConnectorResourceContext resource)
        {
            AwsSettings aws = BuildS3Settings(resource);
            AmazonS3BlobClient client;
            try
            {
                client = new AmazonS3BlobClient(aws);
            }
            catch (ArgumentException e)
            {
                throw new ConnectorException("invalid_setting", "The S3 settings are invalid: " + e.Message, e);
            }

            IStoragePresigner? presigner = aws.HasCredentials ? new S3Presigner(aws) : null;
            string reason = aws.HasCredentials ? String.Empty : "Presigning requires the accessKeyPair secret; this resource uses anonymous access.";
            return new StorageSession(StorageProviderEnum.AmazonS3, client, presigner, reason, aws.Bucket, false);
        }

        private static StorageSession CreateAzureSession(ConnectorResourceContext resource)
        {
            ResourceSettings settings = resource.Settings;
            string container = settings.GetRequiredString(StorageSettingKeys.Container);
            string? connectionString = resource.Secrets.Get(StorageSettingKeys.SlotConnectionString, StorageSettingKeys.FieldConnectionString);
            string? accountKey = resource.Secrets.Get(StorageSettingKeys.SlotAccessKey, StorageSettingKeys.FieldToken);

            try
            {
                BlobClientOptions clientOptions = BuildAzureClientOptions();
                BlobServiceClient? service = null;
                if (!String.IsNullOrEmpty(connectionString))
                {
                    service = new BlobServiceClient(connectionString, clientOptions);
                }
                else if (!String.IsNullOrEmpty(accountKey))
                {
                    string account = settings.GetRequiredString(StorageSettingKeys.AccountName);
                    string endpoint = ResolveAzureEndpoint(settings) ?? throw new ConnectorException("setting_missing", "Required setting 'accountName' is not set.");
                    service = new BlobServiceClient(new Uri(endpoint), new StorageSharedKeyCredential(account, accountKey), clientOptions);
                }

                if (service != null)
                {
                    BlobContainerClient containerClient = service.GetBlobContainerClient(container);
                    AzureBlobClient client = new AzureBlobClient(service, containerClient);
                    return new StorageSession(StorageProviderEnum.AzureBlob, client, new AzureBlobPresigner(containerClient), null, container, false);
                }
            }
            catch (FormatException e)
            {
                throw new ConnectorException("invalid_credentials", "The Azure credentials are malformed (account key must be base64; connection string must be well formed).", e);
            }
            catch (ArgumentException e)
            {
                throw new ConnectorException("invalid_credentials", "The Azure settings or credentials are invalid: " + e.GetType().Name + ".", e);
            }

            throw new ConnectorException("invalid_credentials", "Bind either the accessKey secret (with accountName) or the connectionString secret.");
        }

        private static StorageSession CreateGcsSession(ConnectorResourceContext resource)
        {
            ResourceSettings settings = resource.Settings;
            string bucket = settings.GetRequiredString(StorageSettingKeys.Bucket);
            string projectId = settings.GetRequiredString(StorageSettingKeys.ProjectId);
            string? endpoint = NormalizeGcsEndpoint(settings.GetString(StorageSettingKeys.CustomEndpoint));
            string? json = resource.Secrets.Get(StorageSettingKeys.SlotServiceAccountJson, StorageSettingKeys.FieldJson);
            if (String.IsNullOrEmpty(json)) throw new ConnectorException("invalid_credentials", "Bind the serviceAccountJson secret.");

            GcpBlobSettings gcp = new GcpBlobSettings();
            gcp.ProjectId = projectId;
            gcp.Bucket = bucket;
            gcp.JsonCredentials = json;
            gcp.CustomEndpoint = endpoint;

            try
            {
                GcpBlobClient client = new GcpBlobClient(gcp);
                GcsPresigner presigner = new GcsPresigner(json, bucket, endpoint == null ? null : new Uri(endpoint));
                return new StorageSession(StorageProviderEnum.GoogleCloud, client, presigner, null, bucket, true);
            }
            catch (Exception e) when (!(e is ConnectorException) && !(e is OperationCanceledException))
            {
                throw new ConnectorException("invalid_credentials", "The serviceAccountJson secret is not a usable service account key (" + e.GetType().Name + ").", e);
            }
        }

        #endregion
    }
}
