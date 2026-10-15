namespace Hugo.Connectors.Storage
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the shared storage tools. Resolves the pooled Blobject session, applies the call timeout, and converts
    /// provider failures into tool errors with stable codes. A cancellation requested by the caller propagates as
    /// <see cref="OperationCanceledException"/>; the call timeout returns a <c>timeout</c> error. Thread-safe.
    /// </summary>
    public abstract class StorageToolBase : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Provider this tool instance serves.
        /// </summary>
        public StorageProviderEnum Provider { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="descriptor">Descriptor.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected StorageToolBase(StorageProviderEnum provider, ToolDescriptor descriptor) : base(descriptor)
        {
            Provider = provider;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();

            Stopwatch stopwatch = Stopwatch.StartNew();
            string container = ContainerNameOf(context.Resource);
            string? key = arguments.GetString("key") ?? arguments.GetString("sourceKey");

            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(context.Timeout);
                try
                {
                    StorageOptions options = StorageOptions.FromSettings(context.Resource.Settings);
                    StorageSession session = await StorageClientFactory.GetSessionAsync(Provider, context.Resource, cts.Token).ConfigureAwait(false);
                    ToolResult result = await ExecuteCoreAsync(context, arguments, session, options, cts.Token).ConfigureAwait(false);
                    result.Metrics["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
                    return result;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ToolResult.Error(
                        "timeout",
                        Descriptor.Name + " timed out after " + context.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds. Narrow the prefix or request less data.");
                }
                catch (ConnectorException e)
                {
                    return ToolResult.Error(e.Code, e.Message);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    ConnectorException mapped = StorageErrors.Map(e, container, key);
                    if (mapped.Code == "permission_denied" || mapped.Code == "connection_failed") StorageClientFactory.InvalidateSession(context.Resource);
                    context.Resource.Logger.Warn(Descriptor.Name + " failed on " + context.Resource.Name + ": " + mapped.Code + " (" + e.GetType().Name + ")");
                    return ToolResult.Error(mapped.Code, mapped.Message);
                }
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Execute the call with a resolved session.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="session">Pooled session; do not dispose.</param>
        /// <param name="options">Resource limits and restriction.</param>
        /// <param name="token">Cancellation token that also fires at the call timeout.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(
            ConnectorInvocationContext context,
            ToolArguments arguments,
            StorageSession session,
            StorageOptions options,
            CancellationToken token);

        /// <summary>
        /// Check a key argument during classification. Returns a rejected classification when the key is malformed or
        /// outside the prefix restriction, otherwise null.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="value">Key or prefix value.</param>
        /// <param name="argumentName">Argument name.</param>
        /// <param name="isPrefix">Whether the value is a listing prefix (empty allowed).</param>
        /// <returns>Rejection, or null.</returns>
        protected static EffectClassification? CheckKey(ConnectorInvocationContext context, string? value, string argumentName, bool isPrefix)
        {
            string restriction = RestrictionOf(context);
            try
            {
                if (isPrefix) StorageKeyPolicy.ResolvePrefix(value, restriction);
                else StorageKeyPolicy.ValidateKey(value, restriction, argumentName);
                return null;
            }
            catch (ConnectorException e)
            {
                return EffectClassification.Reject(e.Message);
            }
        }

        /// <summary>
        /// Describe the bucket or container of the resource for approval summaries, for example
        /// <c>bucket 'acme-assets' (assets)</c>.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <returns>Text.</returns>
        protected string Where(ConnectorInvocationContext context)
        {
            if (context == null) return String.Empty;
            string noun = Provider == StorageProviderEnum.AzureBlob ? "container" : "bucket";
            string name = ContainerNameOf(context.Resource);
            string alias = String.IsNullOrEmpty(context.BindingAlias) ? context.Resource.Name : context.BindingAlias;
            string text = noun + " '" + name + "'";
            return String.IsNullOrEmpty(alias) ? text : text + " (" + alias + ")";
        }

        /// <summary>
        /// Prefix restriction of the resource.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <returns>Restriction; empty when none.</returns>
        protected static string RestrictionOf(ConnectorInvocationContext context)
        {
            if (context == null) return String.Empty;
            return (context.Resource.Settings.GetString(StorageSettingKeys.PrefixRestriction) ?? String.Empty).Trim();
        }

        /// <summary>
        /// Effective byte limit for text output: the smaller of the resource read limit and the call's output limit.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="options">Options.</param>
        /// <returns>Bytes.</returns>
        protected static int OutputBudget(ConnectorInvocationContext context, StorageOptions options)
        {
            return Math.Min(options.MaxObjectReadBytes, context.Limits.MaxBytes);
        }

        /// <summary>
        /// Convert Blobject metadata to an entry. Zero-byte keys ending with '/' are reported as folders (folder markers).
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when metadata is null.</exception>
        protected static StorageObjectInfo ToInfo(BlobMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            StorageObjectInfo info = new StorageObjectInfo();
            info.Key = metadata.Key ?? String.Empty;
            info.Size = metadata.ContentLength;
            info.IsFolder = metadata.IsFolder || (info.Key.EndsWith('/') && metadata.ContentLength == 0);
            info.ContentType = String.IsNullOrEmpty(metadata.ContentType) ? null : metadata.ContentType;
            info.ETag = String.IsNullOrEmpty(metadata.ETag) ? null : metadata.ETag.Trim('"');
            info.LastModifiedUtc = metadata.LastUpdateUtc ?? metadata.CreatedUtc;
            info.CreatedUtc = metadata.LastUpdateUtc != null && metadata.CreatedUtc != metadata.LastUpdateUtc ? metadata.CreatedUtc : null;
            return info;
        }

        /// <summary>
        /// Format a UTC timestamp as ISO 8601, or an empty string.
        /// </summary>
        /// <param name="value">Timestamp.</param>
        /// <returns>Text.</returns>
        protected static string FormatTime(DateTime? value)
        {
            if (value == null) return String.Empty;
            DateTime utc = value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value;
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Private-Methods

        private string ContainerNameOf(ConnectorResourceContext resource)
        {
            string key = Provider == StorageProviderEnum.AzureBlob ? StorageSettingKeys.Container : StorageSettingKeys.Bucket;
            return resource.Settings.GetString(key) ?? String.Empty;
        }

        #endregion
    }
}
