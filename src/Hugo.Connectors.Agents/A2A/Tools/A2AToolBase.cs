namespace Hugo.Connectors.Agents.A2A.Tools
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Voltaic.A2A;

    /// <summary>
    /// Base for the <c>a2a-agent</c> tools. Applies the shorter of the call timeout and the resource's
    /// <c>timeoutSeconds</c>, reuses one pooled <see cref="A2ARemoteSession"/> per resource configuration, converts
    /// expected failures into tool errors with stable codes, and lets cancellation by the caller propagate as
    /// <see cref="OperationCanceledException"/>. Thread-safe.
    /// </summary>
    public abstract class A2AToolBase : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Session pool key suffix used by the <c>a2a-agent</c> connector.
        /// </summary>
        public static readonly string SessionSuffix = "a2a";

        /// <summary>
        /// Connector options shared with the owning connector. Never null.
        /// </summary>
        public A2AAgentConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        protected A2AToolBase(ToolDescriptor descriptor, A2AAgentConnectorOptions options) : base(descriptor)
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
            A2AAgentResourceOptions resource = A2AAgentResourceOptions.FromSettings(context.Resource.Settings);
            TimeSpan limit = resource.Timeout < context.Timeout ? resource.Timeout : context.Timeout;
            string key = SessionKey(context.Resource);
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(limit);
                ToolResult result;
                try
                {
                    A2ARemoteSession session = await context.Resource.Sessions.GetOrCreateAsync(key, ct => A2ARemoteSession.ConnectAsync(resource, context.Resource, ct), timeout.Token).ConfigureAwait(false);
                    result = await ExecuteCoreAsync(context, resource, session, arguments, limit, token, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    result = ToolResult.Error(
                        ConnectorErrorCodes.Timeout,
                        Descriptor.Name + " did not finish within " + limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds. The remote agent may be slow or unreachable.");
                }
                catch (ConnectorException e)
                {
                    if (e.Code == ConnectorErrorCodes.ConnectionFailed) context.Resource.Sessions.Invalidate(key);
                    result = ToolResult.Error(e.Code, e.Message);
                    result.RetryAfterSeconds = e.RetryAfterSeconds;
                }
                catch (A2AProtocolException e)
                {
                    result = MapProtocolError(e);
                }
                catch (HttpRequestException e)
                {
                    context.Resource.Sessions.Invalidate(key);
                    result = MapHttpError(e, resource.CardUrl?.GetLeftPart(UriPartial.Authority) ?? "(no agentCardUrl)");
                }
                catch (JsonException e)
                {
                    result = ToolResult.Error(ConnectorErrorCodes.UpstreamError, "The remote agent sent a response Hugo could not read: " + TextClip.Clip(e.Message, 300));
                }
                catch (IOException e)
                {
                    context.Resource.Sessions.Invalidate(key);
                    result = ToolResult.Error(ConnectorErrorCodes.ConnectionFailed, "The connection to the remote agent failed: " + TextClip.Clip(e.Message, 300));
                }

                result.Metrics["latencyMs"] = sw.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        /// <summary>
        /// Pool key of a resource's A2A session.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static string SessionKey(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.SessionKey(SessionSuffix);
        }

        /// <summary>
        /// Map an A2A protocol error to a tool error.
        /// </summary>
        /// <param name="e">Error.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static ToolResult MapProtocolError(A2AProtocolException e)
        {
            ArgumentNullException.ThrowIfNull(e);
            string remote = TextClip.Clip(TextClip.OneLine(UntrustedContent.Neutralize(e.Message)), 500);
            string code = ((int)e.ErrorCode).ToString(CultureInfo.InvariantCulture);
            switch (e.ErrorCode)
            {
                case A2AErrorCode.TaskNotFound:
                    return ToolResult.Error(ConnectorErrorCodes.NotFound, "The remote agent has no such task (A2A " + code + ": " + remote + ").");
                case A2AErrorCode.InvalidParams:
                case A2AErrorCode.InvalidRequest:
                    return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "The remote agent rejected the request (A2A " + code + ": " + remote + ").");
                case A2AErrorCode.UnsupportedOperation:
                case A2AErrorCode.MethodNotFound:
                case A2AErrorCode.ContentTypeNotSupported:
                case A2AErrorCode.VersionNotSupported:
                case A2AErrorCode.ExtensionSupportRequired:
                    return ToolResult.Error(ConnectorErrorCodes.NotSupported, "The remote agent does not support this request (A2A " + code + ": " + remote + ").");
                default:
                    return ToolResult.Error(ConnectorErrorCodes.UpstreamError, "The remote agent returned an error (A2A " + code + ": " + remote + ").");
            }
        }

        /// <summary>
        /// Map an HTTP failure to a tool error.
        /// </summary>
        /// <param name="e">Failure.</param>
        /// <param name="origin">Origin of the agent, for messages.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static ToolResult MapHttpError(HttpRequestException e, string origin)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e.StatusCode == null) return ToolResult.Error(ConnectorErrorCodes.ConnectionFailed, "Could not reach the remote agent at " + origin + ": " + TextClip.Clip(e.Message, 300));
            string statusText = "HTTP " + ((int)e.StatusCode.Value).ToString(CultureInfo.InvariantCulture);
            if (e.StatusCode == HttpStatusCode.Unauthorized) return ToolResult.Error(ConnectorErrorCodes.AuthFailed, "The remote agent at " + origin + " rejected the credentials (" + statusText + "). Check the secret bound to the bearerToken slot.");
            if (e.StatusCode == HttpStatusCode.Forbidden) return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "The remote agent at " + origin + " refused access (" + statusText + ").");
            if (e.StatusCode == HttpStatusCode.NotFound) return ToolResult.Error(ConnectorErrorCodes.NotFound, "The remote agent at " + origin + " answered " + statusText + "; check agentCardUrl.");
            if (e.StatusCode == HttpStatusCode.TooManyRequests) return ToolResult.Error(ConnectorErrorCodes.RateLimited, "The remote agent at " + origin + " is throttling requests (" + statusText + ").");
            return ToolResult.Error(ConnectorErrorCodes.UpstreamError, "The remote agent at " + origin + " answered " + statusText + ".");
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Execute the call.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="resource">Parsed resource settings.</param>
        /// <param name="session">Session.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="limit">Effective timeout.</param>
        /// <param name="callerToken">The caller's token, without the timeout, so a tool can tell a timeout from a cancellation.</param>
        /// <param name="token">Cancellation token including the timeout.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, A2AAgentResourceOptions resource, A2ARemoteSession session, ToolArguments arguments, TimeSpan limit, CancellationToken callerToken, CancellationToken token);

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

        /// <summary>
        /// Build the text and structured result for a task payload.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="payload">Payload.</param>
        /// <returns>Result.</returns>
        protected static ToolResult TaskResult(ConnectorInvocationContext context, Payloads.A2ATaskPayload payload)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(payload);
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(A2ATaskRenderer.RenderText(payload), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.StructuredJson = AgentsJson.Write(payload);
            result.Metrics["artifacts"] = payload.ArtifactCount;
            return result;
        }

        #endregion
    }
}
