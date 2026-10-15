namespace Hugo.Connectors.Storage
{
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Creates the shared storage tool set. Every storage connector exposes the same tool names with the same input
    /// schemas. Thread-safe.
    /// </summary>
    public static class StorageToolSet
    {
        #region Public-Methods

        /// <summary>
        /// Create the tools for one provider, in display order.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <returns>Tools.</returns>
        public static List<IConnectorTool> Create(StorageProviderEnum provider)
        {
            return new List<IConnectorTool>
            {
                new StorageListTool(provider),
                new StorageGetMetadataTool(provider),
                new StorageReadTool(provider),
                new StorageStatsTool(provider),
                new StoragePresignUrlTool(provider),
                new StorageWriteTool(provider),
                new StorageCopyTool(provider),
                new StorageDeleteTool(provider)
            };
        }

        #endregion
    }
}
