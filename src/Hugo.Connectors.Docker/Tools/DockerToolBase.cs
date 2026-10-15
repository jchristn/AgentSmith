namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for Docker tools: option parsing, classification, pooled sessions, the call deadline, and conversion of
    /// failures to error results. Execution re-checks the classification, so a rejected call never reaches the daemon
    /// and a ReadOnly binding never runs a change (defense in depth). Thread-safe; derived tools keep no per-call state
    /// in fields.
    /// </summary>
    public abstract class DockerToolBase : ConnectorToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Descriptor.</param>
        protected DockerToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public sealed override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            DockerOptions options = context == null ? new DockerOptions() : DockerOptions.FromSettings(context.Resource.Settings);
            try
            {
                return ClassifyCall(context, arguments ?? new ToolArguments("{}"), options);
            }
            catch (ConnectorException e)
            {
                return new EffectClassification(EffectEnum.Destructive, "the arguments could not be read (" + e.Message + "), so the call is treated as Destructive", Descriptor.Title + " on " + TargetName(context));
            }
        }

        /// <inheritdoc />
        public sealed override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();
            DockerOptions options = DockerOptions.FromSettings(context.Resource.Settings);

            EffectClassification classification = Classify(context, arguments);
            if (classification.Rejected) return ToolResult.Error("rejected", classification.Reason);
            if (context.AccessMode == AccessModeEnum.ReadOnly && classification.Effect != EffectEnum.Read)
                return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "This binding is ReadOnly and the call is " + classification.Effect + " (" + classification.Reason + ").");

            TimeSpan timeout = CallTimeout(context, options, arguments);
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(timeout);
                try
                {
                    return await ExecuteCoreAsync(context, arguments, options, deadline.Token).ConfigureAwait(false);
                }
                catch (ConnectorException e)
                {
                    if (e.Code == ConnectorErrorCodes.ConnectionFailed) context.Resource.Sessions.Invalidate(context.Resource.SessionKey());
                    ToolResult error = ToolResult.Error(e.Code, e.Message);
                    error.RetryAfterSeconds = e.RetryAfterSeconds;
                    return error;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ToolResult.Error(ConnectorErrorCodes.Timeout, Descriptor.Name + " did not finish within " + (int)timeout.TotalSeconds + " seconds.");
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    if (DockerErrorMapper.IsConnectionBroken(e)) context.Resource.Sessions.Invalidate(context.Resource.SessionKey());
                    ConnectorException mapped = DockerErrorMapper.Map(e, Descriptor.Name, options.Endpoint);
                    return ToolResult.Error(mapped.Code, mapped.Message);
                }
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Classify a call.
        /// </summary>
        /// <param name="context">Context, or null in offline previews.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Resource options.</param>
        /// <returns>Classification.</returns>
        protected abstract EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options);

        /// <summary>
        /// Execute the tool.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Resource options.</param>
        /// <param name="token">Cancellation token (cancelled at the call deadline).</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token);

        /// <summary>
        /// The call deadline: by default the smaller of <c>requestTimeoutSeconds</c> and the context timeout.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Timeout.</returns>
        protected virtual TimeSpan CallTimeout(ConnectorInvocationContext context, DockerOptions options, ToolArguments arguments)
        {
            TimeSpan t = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            return context.Timeout < t ? context.Timeout : t;
        }

        /// <summary>
        /// Get the pooled session for the resource.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session. Do not dispose.</returns>
        protected static Task<DockerSession> GetSessionAsync(ConnectorInvocationContext context, DockerOptions options, CancellationToken token)
        {
            ConnectorResourceContext resource = context.Resource;
            return resource.Sessions.GetOrCreateAsync(resource.SessionKey(), ct => DockerSessionFactory.ConnectAsync(resource, options, ct), token);
        }

        /// <summary>
        /// Read and check a container reference argument.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <returns>Reference without a leading slash.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when missing or malformed.</exception>
        protected static string Container(ToolArguments arguments, string name = "container")
        {
            string? value = arguments.GetString(name)?.Trim();
            if (String.IsNullOrEmpty(value)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + name + "' is required (a container name or ID).");
            if (!DockerFormat.IsContainerReference(value)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "'" + DockerFormat.Shorten(value, 80) + "' is not a container name or ID.");
            return value.TrimStart('/');
        }

        /// <summary>
        /// Read a trimmed optional string argument.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Name.</param>
        /// <returns>Value, or null when absent or blank.</returns>
        protected static string? Optional(ToolArguments arguments, string name)
        {
            string? v = arguments.GetString(name);
            if (String.IsNullOrWhiteSpace(v)) return null;
            return v.Trim();
        }

        /// <summary>
        /// Name used for the target in summaries: the binding alias, or the resource name.
        /// </summary>
        /// <param name="context">Context. May be null.</param>
        /// <returns>Name.</returns>
        protected static string TargetName(ConnectorInvocationContext? context)
        {
            if (context == null) return "the Docker host";
            if (!String.IsNullOrEmpty(context.BindingAlias)) return context.BindingAlias;
            return String.IsNullOrEmpty(context.Resource.Name) ? "the Docker host" : context.Resource.Name;
        }

        /// <summary>
        /// A Read classification with a summary.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <param name="summary">Summary.</param>
        /// <returns>Classification.</returns>
        protected static EffectClassification Read(string reason, string summary)
        {
            return new EffectClassification(EffectEnum.Read, reason, summary);
        }

        #endregion
    }
}
