namespace Hugo.Connectors.Storage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_copy</c>: copy one object to another key in the same bucket or container. Blobject has no server-side
    /// copy, so the content is read into Hugo's memory and written back, bounded by <see cref="MaxCopyBytes"/>. Refuses to replace an existing
    /// destination unless <c>overwrite</c> is true; such a call is classified Destructive, otherwise Write. Thread-safe.
    /// </summary>
    public class StorageCopyTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_copy";

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"sourceKey\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1024,\"description\":\"Full key of the object to copy.\"},"
            + "\"destinationKey\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1024,\"description\":\"Full key to copy to, in the same bucket or container.\"},"
            + "\"overwrite\":{\"type\":\"boolean\",\"description\":\"Replace the destination if it exists. Default false: the call fails with already_exists instead.\"}"
            + "},\"required\":[\"sourceKey\",\"destinationKey\"],\"additionalProperties\":false}";

        /// <summary>
        /// Largest object the tool copies, in bytes. Minimum 1, maximum 5 GiB, default 256 MiB.
        /// </summary>
        public long MaxCopyBytes
        {
            get
            {
                return _MaxCopyBytes;
            }
            set
            {
                _MaxCopyBytes = Math.Clamp(value, 1L, 5L * 1024L * 1024L * 1024L);
            }
        }

        #endregion

        #region Private-Members

        private long _MaxCopyBytes = 256L * 1024L * 1024L;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageCopyTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? source = arguments.GetString("sourceKey");
            string? destination = arguments.GetString("destinationKey");
            EffectClassification? rejected = CheckKey(context, source, "sourceKey", false) ?? CheckKey(context, destination, "destinationKey", false);
            if (rejected != null) return rejected;
            bool overwrite = arguments.GetBool("overwrite", false);
            string summary = "Copy '" + source + "' to '" + destination + "' in " + Where(context) + (overwrite ? ", replacing the destination if it exists" : ", only if the destination does not exist");
            if (overwrite) return new EffectClassification(EffectEnum.Destructive, "overwrite=true may replace an existing object", summary);
            return new EffectClassification(EffectEnum.Write, "creates a new object; refused if the destination exists", summary);
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
            string source = StorageKeyPolicy.ValidateKey(arguments.GetString("sourceKey"), options.PrefixRestriction, "sourceKey");
            string destination = StorageKeyPolicy.ValidateKey(arguments.GetString("destinationKey"), options.PrefixRestriction, "destinationKey");
            if (String.Equals(source, destination, StringComparison.Ordinal))
                throw new ConnectorException("invalid_argument", "sourceKey and destinationKey are the same.");
            if (destination.EndsWith('/')) throw new ConnectorException("invalid_argument", "Argument 'destinationKey' must name an object, not a folder (it ends with '/').");
            bool overwrite = arguments.GetBool("overwrite", false);

            BlobMetadata md = await session.Client.GetMetadataAsync(source, token).ConfigureAwait(false);
            if (md.ContentLength > _MaxCopyBytes)
                throw new ConnectorException("too_large", "Object '" + source + "' is " + StorageContent.FormatBytes(md.ContentLength) + "; storage_copy handles objects up to " + StorageContent.FormatBytes(_MaxCopyBytes) + ".");

            bool exists = await session.Client.ExistsAsync(destination, token).ConfigureAwait(false);
            if (exists && !overwrite)
                throw new ConnectorException("already_exists", "Object '" + destination + "' already exists in '" + session.ContainerName + "'. Nothing was copied. Call again with overwrite=true only if replacing it is intended.");

            string contentType = String.IsNullOrEmpty(md.ContentType) ? StorageContent.GuessContentType(source, "application/octet-stream") : md.ContentType;
            byte[] data = await session.Client.GetAsync(source, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await session.Client.WriteAsync(destination, contentType, data, token).ConfigureAwait(false);

            StorageMutationResult mutation = new StorageMutationResult();
            mutation.Operation = "copy";
            mutation.Container = session.ContainerName;
            mutation.Key = destination;
            mutation.SourceKey = source;
            mutation.Bytes = data.LongLength;
            mutation.Overwrote = exists;
            mutation.ContentType = contentType;

            string text = "Copied '" + source + "' to '" + destination + "' in '" + session.ContainerName + "' (" + data.LongLength + " bytes" + (exists ? ", replaced the existing destination" : String.Empty) + ").\n";
            ToolResult result = ToolResult.FromText(text, StorageJson.Serialize(mutation));
            result.Metrics["bytes"] = data.LongLength;
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Copy object";
            d.Description =
                "Copy one object to another key in the same bucket or container, keeping its content type. Fails if the destination "
                + "exists unless overwrite=true (which makes the call destructive). Both keys must be inside the resource's prefix "
                + "restriction. To move an object, copy it and then delete the source with storage_delete.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Write;
            d.ClassifiedPerCall = true;
            d.OutputDescription = "Source, destination, bytes copied, and whether the destination was replaced.";
            return d;
        }

        #endregion
    }
}
