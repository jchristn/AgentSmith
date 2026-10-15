namespace Hugo.Connectors.Storage
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_write</c>: create an object from text or base64 content. Refuses to replace an existing object unless
    /// <c>overwrite</c> is true. A call with <c>overwrite = true</c> is classified Destructive (classification cannot
    /// check existence without a network call, so it assumes the worst); otherwise Write. Thread-safe.
    /// </summary>
    public class StorageWriteTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_write";

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1024,\"description\":\"Full object key to write, for example 'reports/2026-10-14.md'.\"},"
            + "\"content\":{\"type\":\"string\",\"description\":\"Object content: UTF-8 text, or base64 when encoding is base64. May be empty.\"},"
            + "\"encoding\":{\"type\":\"string\",\"enum\":[\"text\",\"base64\"],\"description\":\"How content is encoded. Default text.\"},"
            + "\"contentType\":{\"type\":\"string\",\"maxLength\":255,\"description\":\"Content type to store. Default: guessed from the key extension (text/plain for text, application/octet-stream for base64).\"},"
            + "\"overwrite\":{\"type\":\"boolean\",\"description\":\"Replace the object if it already exists. Default false: the call fails with already_exists instead.\"}"
            + "},\"required\":[\"key\",\"content\"],\"additionalProperties\":false}";

        /// <summary>
        /// Maximum decoded content size in bytes. Minimum 1, maximum 1 GiB, default 16 MiB.
        /// </summary>
        public long MaxWriteBytes
        {
            get
            {
                return _MaxWriteBytes;
            }
            set
            {
                _MaxWriteBytes = Math.Clamp(value, 1L, 1024L * 1024L * 1024L);
            }
        }

        #endregion

        #region Private-Members

        private long _MaxWriteBytes = 16L * 1024L * 1024L;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageWriteTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
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
            string? encoding = arguments.GetString("encoding");
            if (encoding != null && encoding != "text" && encoding != "base64") return EffectClassification.Reject("argument 'encoding' must be text or base64");

            bool overwrite = arguments.GetBool("overwrite", false);
            string content = arguments.GetString("content") ?? String.Empty;
            long bytes = encoding == "base64" ? (content.Length / 4) * 3 : Encoding.UTF8.GetByteCount(content);
            string summary = "Write " + bytes + " bytes to '" + key + "' in " + Where(context) + (overwrite ? ", replacing it if it exists" : ", only if it does not exist");
            if (overwrite) return new EffectClassification(EffectEnum.Destructive, "overwrite=true may replace an existing object", summary);
            return new EffectClassification(EffectEnum.Write, "creates a new object; refused if the key exists", summary);
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
            if (key.EndsWith('/')) throw new ConnectorException("invalid_argument", "Argument 'key' must name an object, not a folder (it ends with '/').");
            string encoding = (arguments.GetString("encoding") ?? "text").Trim().ToLowerInvariant();
            if (encoding != "text" && encoding != "base64") throw new ConnectorException("invalid_argument", "Argument 'encoding' must be text or base64.");
            if (!arguments.Has("content")) throw new ConnectorException("invalid_argument", "Argument 'content' is required.");
            string content = arguments.GetString("content") ?? String.Empty;
            bool overwrite = arguments.GetBool("overwrite", false);

            byte[] data;
            if (encoding == "base64")
            {
                try
                {
                    data = Convert.FromBase64String(content);
                }
                catch (FormatException)
                {
                    throw new ConnectorException("invalid_argument", "Argument 'content' is not valid base64.");
                }
            }
            else
            {
                data = Encoding.UTF8.GetBytes(content);
            }
            if (data.LongLength > _MaxWriteBytes)
                throw new ConnectorException("too_large", "Content is " + data.LongLength + " bytes; storage_write accepts at most " + _MaxWriteBytes + " bytes.");

            string fallbackType = encoding == "base64" ? "application/octet-stream" : "text/plain; charset=utf-8";
            string contentType = arguments.GetString("contentType") ?? StorageContent.GuessContentType(key, fallbackType);

            bool exists = await session.Client.ExistsAsync(key, token).ConfigureAwait(false);
            if (exists && !overwrite)
                throw new ConnectorException("already_exists", "Object '" + key + "' already exists in '" + session.ContainerName + "'. Nothing was written. Call again with overwrite=true only if replacing it is intended.");

            token.ThrowIfCancellationRequested();
            await session.Client.WriteAsync(key, contentType, data, token).ConfigureAwait(false);

            StorageMutationResult mutation = new StorageMutationResult();
            mutation.Operation = "write";
            mutation.Container = session.ContainerName;
            mutation.Key = key;
            mutation.Bytes = data.LongLength;
            mutation.Overwrote = exists;
            mutation.ContentType = contentType;

            string text = (exists ? "Replaced" : "Created") + " '" + key + "' in '" + session.ContainerName + "': " + data.LongLength + " bytes, " + contentType + ".\n";
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
            d.Title = "Write object";
            d.Description =
                "Create an object from text or base64 content. By default the call fails if the key already exists; set "
                + "overwrite=true only when you intend to replace it (that makes the call destructive and it may need approval). "
                + "State the full key before writing. The key must be inside the resource's prefix restriction.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Write;
            d.ClassifiedPerCall = true;
            d.OutputDescription = "Whether the object was created or replaced, its size, and content type.";
            return d;
        }

        #endregion
    }
}
