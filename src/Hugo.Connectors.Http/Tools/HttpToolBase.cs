namespace Hugo.Connectors.Http.Tools
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the <c>http</c> tools. Applies the shorter of the call timeout and the resource's <c>timeoutSeconds</c>,
    /// converts expected failures into tool errors (<c>timeout</c> and the codes of <see cref="ConnectorException"/>),
    /// and lets cancellation by the caller propagate as <see cref="OperationCanceledException"/>. Thread-safe.
    /// </summary>
    public abstract class HttpToolBase : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Connector options shared with the owning connector. Never null.
        /// </summary>
        public HttpConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        protected HttpToolBase(ToolDescriptor descriptor, HttpConnectorOptions options) : base(descriptor)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when context or arguments is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the caller cancels the token.</exception>
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();

            Stopwatch sw = Stopwatch.StartNew();
            HttpResourceOptions resource = HttpResourceOptions.FromSettings(context.Resource.Settings);
            TimeSpan limit = resource.Timeout < context.Timeout ? resource.Timeout : context.Timeout;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(limit);
                ToolResult result;
                try
                {
                    result = await ExecuteCoreAsync(context, resource, arguments, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    result = ToolResult.Error(
                        ConnectorErrorCodes.Timeout,
                        Descriptor.Name + " did not finish within " + limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                        + " seconds. The server may be slow or unreachable; try a narrower request or check availability with http_health_check.");
                }
                catch (ConnectorException e)
                {
                    result = ToolResult.Error(e.Code, e.Message);
                    result.RetryAfterSeconds = e.RetryAfterSeconds;
                }

                result.Metrics["latencyMs"] = sw.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Execute the call. The token already includes the timeout.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="resource">Parsed resource options.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="token">Cancellation token including the timeout.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, HttpResourceOptions resource, ToolArguments arguments, CancellationToken token);

        /// <summary>
        /// Name used for the target in approval summaries: the binding alias, else the resource name.
        /// </summary>
        /// <param name="context">Context, or null.</param>
        /// <returns>Target name.</returns>
        protected static string Target(ConnectorInvocationContext? context)
        {
            if (context == null) return String.Empty;
            return String.IsNullOrEmpty(context.BindingAlias) ? context.Resource.Name : context.BindingAlias;
        }

        #endregion
    }
}
