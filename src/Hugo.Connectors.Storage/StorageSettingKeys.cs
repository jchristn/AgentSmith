namespace Hugo.Connectors.Storage
{
    /// <summary>
    /// Setting keys and secret slot names used by the storage connectors. These are part of the stored resource
    /// contract and are not configurable.
    /// </summary>
    public static class StorageSettingKeys
    {
        /// <summary>
        /// S3 and GCS bucket name.
        /// </summary>
        public const string Bucket = "bucket";

        /// <summary>
        /// S3 region.
        /// </summary>
        public const string Region = "region";

        /// <summary>
        /// S3 endpoint URL for S3-compatible stores.
        /// </summary>
        public const string Endpoint = "endpoint";

        /// <summary>
        /// S3 TLS flag.
        /// </summary>
        public const string UseSsl = "useSsl";

        /// <summary>
        /// Azure storage account name.
        /// </summary>
        public const string AccountName = "accountName";

        /// <summary>
        /// Azure container name.
        /// </summary>
        public const string Container = "container";

        /// <summary>
        /// GCS project identifier.
        /// </summary>
        public const string ProjectId = "projectId";

        /// <summary>
        /// GCS custom endpoint (emulators).
        /// </summary>
        public const string CustomEndpoint = "customEndpoint";

        /// <summary>
        /// Key prefix every object key must start with.
        /// </summary>
        public const string PrefixRestriction = "prefixRestriction";

        /// <summary>
        /// Maximum bytes returned by one read.
        /// </summary>
        public const string MaxObjectReadBytes = "maxObjectReadBytes";

        /// <summary>
        /// Maximum entries returned by one list call.
        /// </summary>
        public const string MaxListResults = "maxListResults";

        /// <summary>
        /// Maximum objects scanned by one stats or folder listing call.
        /// </summary>
        public const string MaxScanObjects = "maxScanObjects";

        /// <summary>
        /// S3 secret slot holding an access key pair.
        /// </summary>
        public const string SlotAccessKeyPair = "accessKeyPair";

        /// <summary>
        /// Azure secret slot holding the account key.
        /// </summary>
        public const string SlotAccessKey = "accessKey";

        /// <summary>
        /// Azure secret slot holding a connection string.
        /// </summary>
        public const string SlotConnectionString = "connectionString";

        /// <summary>
        /// GCS secret slot holding a service account key.
        /// </summary>
        public const string SlotServiceAccountJson = "serviceAccountJson";

        /// <summary>
        /// Secret field: access key ID.
        /// </summary>
        public const string FieldAccessKeyId = "accessKeyId";

        /// <summary>
        /// Secret field: secret access key.
        /// </summary>
        public const string FieldSecretAccessKey = "secretAccessKey";

        /// <summary>
        /// Secret field: session token.
        /// </summary>
        public const string FieldSessionToken = "sessionToken";

        /// <summary>
        /// Secret field: API token (Azure account key).
        /// </summary>
        public const string FieldToken = "token";

        /// <summary>
        /// Secret field: connection string.
        /// </summary>
        public const string FieldConnectionString = "connectionString";

        /// <summary>
        /// Secret field: service account JSON.
        /// </summary>
        public const string FieldJson = "json";
    }
}
