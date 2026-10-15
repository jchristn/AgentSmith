namespace Hugo.Connectors.Logging
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the logging tools. Applies the call timeout, converts expected failures into tool errors
    /// (<c>timeout</c>, <c>rate_limited</c>, <c>auth_failed</c>, and the codes of <see cref="ConnectorException"/>),
    /// and lets cancellation by the caller propagate as <see cref="OperationCanceledException"/>. Thread-safe.
    /// </summary>
    public abstract class LoggingToolBase : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Connector options shared with the owning connector. Never null.
        /// </summary>
        public LoggingConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        protected LoggingToolBase(ToolDescriptor descriptor, LoggingConnectorOptions options) : base(descriptor)
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
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(context.Timeout);
                ToolResult result;
                try
                {
                    result = await ExecuteCoreAsync(context, arguments, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    result = ToolResult.Error(
                        "timeout",
                        Descriptor.Name + " did not finish within " + context.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                        + " seconds. Narrow the time window or the query and try again.");
                }
                catch (LoggingUpstreamException e)
                {
                    result = ToolResult.Error(e.Code, e.Message);
                    result.Metrics["httpStatus"] = e.StatusCode;
                    if (e.RetryAfterSeconds.HasValue) result.Metrics["retryAfterSeconds"] = e.RetryAfterSeconds.Value;
                }
                catch (ConnectorException e)
                {
                    result = ToolResult.Error(e.Code, e.Message);
                }

                result.Metrics["latencyMs"] = sw.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Execute the call. The token already includes the call timeout.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="token">Cancellation token including the timeout.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token);

        /// <summary>
        /// Effective event limit: the requested value, or the default, capped by the resource maximum and the call's
        /// row limit.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="fallback">Default when <c>limit</c> is absent.</param>
        /// <param name="resourceMax">Resource maximum (<c>maxEvents</c>).</param>
        /// <param name="limits">Output limits.</param>
        /// <returns>Limit between 1 and the smallest cap.</returns>
        protected static int ResolveLimit(ToolArguments arguments, int fallback, int resourceMax, OutputLimits limits)
        {
            int cap = Math.Max(1, Math.Min(resourceMax, limits.MaxRows));
            return arguments.GetInt("limit", Math.Min(fallback, cap), 1, cap);
        }

        #endregion
    }
}
