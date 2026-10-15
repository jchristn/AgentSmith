namespace Hugo.Connectors.Storage
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_get_metadata</c> (Read): size, content type, ETag, and timestamps of one object. Thread-safe.
    /// </summary>
    public class StorageGetMetadataTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_get_metadata";

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1024,\"description\":\"Full object key, for example 'data/config.json'.\"}"
            + "},\"required\":[\"key\"],\"additionalProperties\":false}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageGetMetadataTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
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
            return new EffectClassification(EffectEnum.Read, "reading metadata is read-only", "Get metadata of '" + key + "' in " + Where(context));
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
            BlobMetadata md = await session.Client.GetMetadataAsync(key, token).ConfigureAwait(false);
            StorageObjectInfo info = ToInfo(md);
            info.Key = key;

            StringBuilder sb = new StringBuilder();
            sb.Append("Object '").Append(key).Append("' in '").Append(session.ContainerName).Append("'\n");
            sb.Append("size: ").Append(info.Size).Append(" bytes (").Append(StorageContent.FormatBytes(info.Size)).Append(")\n");
            sb.Append("contentType: ").Append(info.ContentType ?? "(not reported)").Append('\n');
            sb.Append("textual: ").Append(StorageContent.IsTextualContentType(info.ContentType) ? "yes" : "no or unknown").Append('\n');
            if (info.ETag != null) sb.Append("etag: ").Append(info.ETag).Append('\n');
            if (info.LastModifiedUtc != null) sb.Append("lastModified: ").Append(FormatTime(info.LastModifiedUtc)).Append('\n');
            if (info.CreatedUtc != null) sb.Append("created: ").Append(FormatTime(info.CreatedUtc)).Append('\n');
            if (info.Size > options.MaxObjectReadBytes)
                sb.Append("note: larger than this resource's read limit (").Append(options.MaxObjectReadBytes).Append(" bytes); storage_read returns it in ranges.\n");

            ToolResult result = ToolResult.FromText(sb.ToString(), StorageJson.Serialize(info));
            result.Metrics["bytes"] = info.Size;
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Get object metadata";
            d.Description =
                "Return the size, content type, ETag, and timestamps of one object without downloading it. Use it before "
                + "storage_read to check size and type, or to confirm an object exists.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "Key, size, content type, ETag, last modified and created times.";
            return d;
        }

        #endregion
    }
}
