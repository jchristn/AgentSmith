namespace Hugo.Connectors.Observability
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the observability tools. Applies the call timeout, converts expected failures into tool errors
    /// (<c>timeout</c>, <c>rate_limited</c>, <c>auth_failed</c>, and the codes of <see cref="ConnectorException"/>), and
    /// lets cancellation by the caller propagate as <see cref="OperationCanceledException"/>. Every tool is Read.
    /// Thread-safe.
    /// </summary>
    public abstract class ObservabilityToolBase : ConnectorToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected ObservabilityToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
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
                        ConnectorErrorCodes.Timeout,
                        Descriptor.Name + " did not finish within " + context.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                        + " seconds. Narrow the query or shorten the time range and try again.");
                }
                catch (ObservabilityUpstreamException e)
                {
                    result = ToolResult.Error(e.Code, e.Message);
                    result.Metrics["httpStatus"] = e.StatusCode;
                    if (e.RetryAfterSeconds.HasValue)
                    {
                        result.RetryAfterSeconds = e.RetryAfterSeconds.Value;
                        result.Metrics["retryAfterSeconds"] = e.RetryAfterSeconds.Value;
                    }
                }
                catch (ConnectorException e)
                {
                    result = ToolResult.Error(e.Code, e.Message);
                    if (e.RetryAfterSeconds.HasValue) result.RetryAfterSeconds = e.RetryAfterSeconds.Value;
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
        /// Build a text result bounded by the output limits, with optional structured JSON.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="structuredJson">Structured JSON, or null.</param>
        /// <param name="limits">Output limits.</param>
        /// <param name="truncated">Whether items were already dropped before rendering.</param>
        /// <returns>Result.</returns>
        protected static ToolResult Finish(string text, string? structuredJson, OutputLimits limits, bool truncated)
        {
            ToolResult result = new ToolResult();
            result.Truncated = truncated;
            result.Text = OutputLimiter.Head(text, limits.MaxBytes, limits.MaxLines, result);
            if (structuredJson != null && System.Text.Encoding.UTF8.GetByteCount(structuredJson) <= limits.MaxBytes * 4) result.StructuredJson = structuredJson;
            else if (structuredJson != null) result.Truncated = true;
            return result;
        }

        /// <summary>
        /// Read a required argument and trim it.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <returns>Value.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when absent or blank.</exception>
        protected static string Required(ToolArguments arguments, string name)
        {
            string? value = arguments.GetString(name);
            if (String.IsNullOrWhiteSpace(value)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + name + "' is required.");
            return value.Trim();
        }

        #endregion
    }
}
