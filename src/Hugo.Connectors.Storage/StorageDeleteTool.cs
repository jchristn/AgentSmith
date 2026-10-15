namespace Hugo.Connectors.Storage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_delete</c> (Destructive): delete one object by its full key. Prefixes and wildcards are not accepted.
    /// Thread-safe.
    /// </summary>
    public class StorageDeleteTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_delete";

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1024,\"description\":\"Full key of the one object to delete. Prefixes and wildcards are not accepted.\"}"
            + "},\"required\":[\"key\"],\"additionalProperties\":false}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageDeleteTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? key = arguments.GetString("key");
            EffectClassification? rejected = CheckKey(context, key, "key", false);
            if (rejected != null) return rejected;
            return new EffectClassification(EffectEnum.Destructive, "deleting an object cannot be undone unless the bucket keeps versions", "Delete '" + key + "' from " + Where(context));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(
            ConnectorInvocationContext context,
            ToolArguments arguments,
            StorageSession session,
            StorageOptions options,
            CancellationToken token)
        {
            string key = StorageKeyPolicy.ValidateKey(arguments.GetString("key"), options.PrefixRestriction);

            BlobMetadata md;
            try
            {
                md = await session.Client.GetMetadataAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception e) when (StorageErrors.IsNotFound(e))
            {
                throw new ConnectorException("not_found", "Object '" + key + "' does not exist in '" + session.ContainerName + "'; nothing was deleted.");
            }

            token.ThrowIfCancellationRequested();
            await session.Client.DeleteAsync(key, token).ConfigureAwait(false);

            StorageMutationResult mutation = new StorageMutationResult();
            mutation.Operation = "delete";
            mutation.Container = session.ContainerName;
            mutation.Key = key;
            mutation.Bytes = md.ContentLength;

            string text = "Deleted '" + key + "' (" + md.ContentLength + " bytes) from '" + session.ContainerName + "'.\n";
            ToolResult result = ToolResult.FromText(text, StorageJson.Serialize(mutation));
            result.Metrics["bytes"] = md.ContentLength;
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Delete object";
            d.Description =
                "Delete exactly one object by its full key. This is destructive and usually requires approval. Confirm the key "
                + "with storage_get_metadata first and state the full key in your request. Prefixes and wildcards are not "
                + "supported; delete objects one at a time.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Destructive;
            d.OutputDescription = "The deleted key and its size.";
            return d;
        }

        #endregion
    }
}
