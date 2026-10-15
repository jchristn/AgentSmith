namespace Hugo.Connectors.Agents.Mcp.Tools
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the <c>mcp</c> tools. Applies the shorter of the call timeout and the resource's <c>timeoutSeconds</c>,
    /// reuses one pooled <see cref="McpRemoteSession"/> per resource configuration, converts expected failures into tool
    /// errors with stable codes, drops broken sessions so the next call reconnects, and lets cancellation by the caller
    /// propagate as <see cref="OperationCanceledException"/>. Thread-safe.
    /// </summary>
    public abstract class McpToolBase : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Session pool key suffix used by the <c>mcp</c> connector.
        /// </summary>
        public static readonly string SessionSuffix = "mcp";

        /// <summary>
        /// Connector options shared with the owning connector. Never null.
        /// </summary>
        public McpConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        protected McpToolBase(ToolDescriptor descriptor, McpConnectorOptions options) : base(descriptor)
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
            McpResourceOptions resource = McpResourceOptions.FromSettings(context.Resource.Settings);
            TimeSpan limit = resource.Timeout < context.Timeout ? resource.Timeout : context.Timeout;
            string key = SessionKey(context.Resource);
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(limit);
                ToolResult result;
                try
                {
                    McpRemoteSession session = await GetSessionAsync(context, resource, timeout.Token).ConfigureAwait(false);
                    result = await ExecuteCoreAsync(context, resource, session, arguments, limit, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    result = ToolResult.Error(
                        ConnectorErrorCodes.Timeout,
                        Descriptor.Name + " did not finish within " + limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                        + " seconds. The remote MCP server may be slow or unreachable; try a smaller request.");
                }
                catch (ConnectorException e)
                {
                    if (e.Code == ConnectorErrorCodes.ConnectionFailed || e.Code == ConnectorErrorCodes.AuthFailed) Drop(context.Resource, key);
                    result = ToolResult.Error(e.Code, e.Message);
                    result.RetryAfterSeconds = e.RetryAfterSeconds;
                }
                catch (McpRemoteException e)
                {
                    result = MapRemoteError(e);
                }
                catch (HttpRequestException e)
                {
                    Drop(context.Resource, key);
                    result = MapHttpError(e, resource.EndpointName());
                }
                catch (IOException e)
                {
                    Drop(context.Resource, key);
                    result = ToolResult.Error(ConnectorErrorCodes.ConnectionFailed, "The connection to the MCP server at " + resource.EndpointName() + " failed: " + TextClip.Clip(e.Message, 300));
                }
                catch (InvalidOperationException e)
                {
                    Drop(context.Resource, key);
                    result = ToolResult.Error(ConnectorErrorCodes.ConnectionFailed, "The MCP session to " + resource.EndpointName() + " is not usable: " + TextClip.Clip(e.Message, 300) + " The next call reconnects.");
                }

                result.Metrics["latencyMs"] = sw.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        /// <summary>
        /// Pool key of a resource's MCP session (also the key of its discovered tool list).
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
        /// Map a JSON-RPC error from the remote server to a tool error.
        /// </summary>
        /// <param name="e">Error.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static ToolResult MapRemoteError(McpRemoteException e)
        {
            ArgumentNullException.ThrowIfNull(e);
            string remote = TextClip.Clip(TextClip.OneLine(UntrustedContent.Neutralize(e.Message)), 500);
            string code = e.RpcCode.ToString(CultureInfo.InvariantCulture);
            if (e.RpcCode == -32601) return ToolResult.Error(ConnectorErrorCodes.NotSupported, "The remote MCP server does not support this operation (JSON-RPC " + code + ": " + remote + ").");
            if (e.RpcCode == -32602) return ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "The remote MCP server rejected the request parameters (JSON-RPC " + code + ": " + remote + "). Check the tool name and the arguments against the schema from mcp_list_tools.");
            if (e.RpcCode == -32002) return ToolResult.Error(ConnectorErrorCodes.NotFound, "The remote MCP server reports that the item was not found (JSON-RPC " + code + ": " + remote + ").");
            return ToolResult.Error(ConnectorErrorCodes.UpstreamError, "The remote MCP server returned an error (JSON-RPC " + code + ": " + remote + ").");
        }

        /// <summary>
        /// Map an HTTP failure to a tool error.
        /// </summary>
        /// <param name="e">Failure.</param>
        /// <param name="endpoint">Endpoint description.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static ToolResult MapHttpError(HttpRequestException e, string endpoint)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e.StatusCode == null) return ToolResult.Error(ConnectorErrorCodes.ConnectionFailed, "Could not reach the MCP server at " + endpoint + ": " + TextClip.Clip(e.Message, 300));
            int status = (int)e.StatusCode.Value;
            string statusText = "HTTP " + status.ToString(CultureInfo.InvariantCulture);
            if (e.StatusCode == HttpStatusCode.Unauthorized) return ToolResult.Error(ConnectorErrorCodes.AuthFailed, "The MCP server at " + endpoint + " rejected the credentials (" + statusText + ").");
            if (e.StatusCode == HttpStatusCode.Forbidden) return ToolResult.Error(ConnectorErrorCodes.PermissionDenied, "The MCP server at " + endpoint + " refused access (" + statusText + ").");
            if (e.StatusCode == HttpStatusCode.NotFound) return ToolResult.Error(ConnectorErrorCodes.NotFound, "The MCP server at " + endpoint + " answered " + statusText + "; the session or endpoint is gone. The next call reconnects.");
            if (e.StatusCode == HttpStatusCode.TooManyRequests) return ToolResult.Error(ConnectorErrorCodes.RateLimited, "The MCP server at " + endpoint + " is throttling requests (" + statusText + ").");
            return ToolResult.Error(ConnectorErrorCodes.UpstreamError, "The MCP server at " + endpoint + " answered " + statusText + ".");
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Execute the call. The token already includes the timeout.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="resource">Parsed resource settings.</param>
        /// <param name="session">Connected session.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="limit">Effective timeout for remote requests.</param>
        /// <param name="token">Cancellation token including the timeout.</param>
        /// <returns>Result.</returns>
        protected abstract Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, McpResourceOptions resource, McpRemoteSession session, ToolArguments arguments, TimeSpan limit, CancellationToken token);

        /// <summary>
        /// The discovered tool list of a session, refreshed per the resource's interval, and published to the
        /// connector's catalog cache for classification.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="resource">Parsed resource settings.</param>
        /// <param name="session">Session.</param>
        /// <param name="force">Fetch even when fresh.</param>
        /// <param name="limit">Timeout per request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The list.</returns>
        protected async Task<McpToolCatalog> GetCatalogAsync(ConnectorInvocationContext context, McpResourceOptions resource, McpRemoteSession session, bool force, TimeSpan limit, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(session);
            McpToolCatalog catalog = await session.GetCatalogAsync(force, resource.RefreshInterval, Options.MaxListPages, limit, token).ConfigureAwait(false);
            Options.Catalogs.Put(SessionKey(context.Resource), catalog, Options.CatalogCacheCapacity);
            return catalog;
        }

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

        #region Private-Methods

        private async Task<McpRemoteSession> GetSessionAsync(ConnectorInvocationContext context, McpResourceOptions resource, CancellationToken token)
        {
            McpRemoteSession.EnsureStdioAllowed(resource, Options);
            string key = SessionKey(context.Resource);
            ConnectorResourceContext target = context.Resource;
            McpRemoteSession session = await target.Sessions.GetOrCreateAsync(key, ct => McpRemoteSession.ConnectAsync(resource, target, Options, ct), token).ConfigureAwait(false);
            if (session.IsConnected) return session;
            target.Sessions.Invalidate(key);
            return await target.Sessions.GetOrCreateAsync(key, ct => McpRemoteSession.ConnectAsync(resource, target, Options, ct), token).ConfigureAwait(false);
        }

        private static void Drop(ConnectorResourceContext resource, string key)
        {
            resource.Sessions.Invalidate(key);
        }

        #endregion
    }
}
