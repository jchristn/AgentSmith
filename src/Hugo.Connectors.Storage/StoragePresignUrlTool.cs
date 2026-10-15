namespace Hugo.Connectors.Storage
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_presign_url</c>: create a time-limited signed URL for one object. A GET URL is Write (it grants
    /// external read access); a PUT URL is Destructive (anyone holding it can replace the object). Blobject's
    /// <c>GenerateUrl</c> is unsigned, so signing uses the provider SDK (SigV4 for S3, a service SAS for Azure, V4
    /// signing for GCS). Thread-safe.
    /// </summary>
    public class StoragePresignUrlTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_presign_url";

        /// <summary>
        /// Default URL lifetime in seconds.
        /// </summary>
        public const int DefaultExpirySeconds = 3600;

        /// <summary>
        /// Maximum URL lifetime in seconds (7 days, the SigV4 and GCS V4 limit).
        /// </summary>
        public const int MaximumExpirySeconds = 604800;

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1024,\"description\":\"Full object key.\"},"
            + "\"expirySeconds\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":604800,\"description\":\"URL lifetime in seconds. Default 3600, maximum 604800 (7 days). Keep it as short as the task allows.\"},"
            + "\"method\":{\"type\":\"string\",\"enum\":[\"GET\",\"PUT\"],\"description\":\"GET (default) lets the holder download the object; PUT lets the holder upload or replace it.\"}"
            + "},\"required\":[\"key\"],\"additionalProperties\":false}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StoragePresignUrlTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
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
            string? method = ParseMethod(arguments.GetString("method"));
            if (method == null) return EffectClassification.Reject("argument 'method' must be GET or PUT");
            int expiry = arguments.GetInt("expirySeconds", DefaultExpirySeconds, 1, MaximumExpirySeconds);
            string summary = "Create a presigned " + method + " URL for '" + key + "' in " + Where(context) + ", valid for " + expiry + " seconds";
            if (method == "PUT")
                return new EffectClassification(EffectEnum.Destructive, "a presigned PUT URL lets anyone holding it replace the object", summary);
            return new EffectClassification(EffectEnum.Write, "a presigned URL grants external read access to anyone holding it", summary);
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
            string? method = ParseMethod(arguments.GetString("method"));
            if (method == null) throw new ConnectorException("invalid_argument", "Argument 'method' must be GET or PUT.");
            int expiry = arguments.GetInt("expirySeconds", DefaultExpirySeconds, 1, MaximumExpirySeconds);

            if (session.Presigner == null)
                throw new ConnectorException("not_supported", "Presigned URLs are not available for this resource. " + session.PresignUnavailableReason);

            if (method == "GET")
            {
                bool exists = await session.Client.ExistsAsync(key, token).ConfigureAwait(false);
                if (!exists) throw new ConnectorException("not_found", "Object '" + key + "' does not exist in '" + session.ContainerName + "'; no URL was created.");
            }

            DateTime expires = DateTime.UtcNow.AddSeconds(expiry);
            StoragePresignMethodEnum verb = method == "PUT" ? StoragePresignMethodEnum.Put : StoragePresignMethodEnum.Get;
            string url = await session.Presigner.PresignAsync(key, verb, TimeSpan.FromSeconds(expiry), token).ConfigureAwait(false);

            StoragePresignResult presign = new StoragePresignResult();
            presign.Container = session.ContainerName;
            presign.Key = key;
            presign.Method = method;
            presign.Url = url;
            presign.ExpiresUtc = expires;

            StringBuilder sb = new StringBuilder();
            sb.Append("Presigned ").Append(method).Append(" URL for '").Append(key).Append("' in '").Append(session.ContainerName).Append("'\n");
            sb.Append("expires: ").Append(expires.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(" (").Append(expiry).Append(" seconds)\n");
            sb.Append("url: ").Append(url).Append('\n');
            sb.Append("Anyone with this URL has ").Append(method == "PUT" ? "upload" : "download").Append(" access until it expires. Share it only with the intended recipient.\n");
            return ToolResult.FromText(sb.ToString(), StorageJson.Serialize(presign));
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Create presigned URL";
            d.Description =
                "Create a time-limited signed URL for one object so someone without credentials can download it (GET) or upload "
                + "it (PUT). This grants external access, so it needs write permission and may need approval; PUT URLs are treated "
                + "as destructive. Use the shortest expiry that works and state who the URL is for.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Write;
            d.ClassifiedPerCall = true;
            d.OutputDescription = "The URL, its method, and its expiry time.";
            return d;
        }

        private static string? ParseMethod(string? value)
        {
            if (String.IsNullOrEmpty(value)) return "GET";
            string upper = value.Trim().ToUpperInvariant();
            if (upper == "GET" || upper == "PUT") return upper;
            return null;
        }

        #endregion
    }
}
