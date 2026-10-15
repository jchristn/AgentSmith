namespace Hugo.Connectors.Storage
{
    /// <summary>
    /// HTTP method a presigned URL authorizes.
    /// </summary>
    public enum StoragePresignMethodEnum
    {
        /// <summary>
        /// Download the object.
        /// </summary>
        Get = 0,

        /// <summary>
        /// Upload (create or replace) the object.
        /// </summary>
        Put = 1
    }
}
