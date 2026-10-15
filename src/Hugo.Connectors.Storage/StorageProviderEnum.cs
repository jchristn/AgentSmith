namespace Hugo.Connectors.Storage
{
    /// <summary>
    /// Object storage provider behind a storage connector.
    /// </summary>
    public enum StorageProviderEnum
    {
        /// <summary>
        /// Amazon S3 and S3-compatible stores (connector type <c>s3</c>).
        /// </summary>
        AmazonS3 = 0,

        /// <summary>
        /// Azure Blob Storage (connector type <c>azure-blob</c>).
        /// </summary>
        AzureBlob = 1,

        /// <summary>
        /// Google Cloud Storage (connector type <c>gcs</c>).
        /// </summary>
        GoogleCloud = 2
    }
}
