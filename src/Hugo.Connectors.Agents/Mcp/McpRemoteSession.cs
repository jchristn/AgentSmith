namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.Mcp.Wire;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// One live connection to an external MCP server (Voltaic <see cref="McpHttpClient"/> for Streamable HTTP, or
    /// <see cref="McpClient"/> for a Stdio child process), pooled per resource through the connector session pool. It
    /// keeps what <c>initialize</c> reported and the discovered tool list. Disposing it ends the remote session and, for
    /// Stdio, stops the child process. Thread-safe.
    /// </summary>
    public class McpRemoteSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Transport in use.
        /// </summary>
        public McpTransportEnum Transport { get; }

        /// <summary>
        /// What the server reported in <c>initialize</c>. Never null.
        /// </summary>
        public McpServerInfo Server { get; private set; } = new McpServerInfo(null);

        /// <summary>
        /// The last discovered tool list, or null before the first discovery.
        /// </summary>
        public McpToolCatalog? Catalog
        {
            get
            {
                return _Catalog;
            }
        }

        /// <summary>
        /// Whether the connection is usable. A dropped connection is replaced on the next call.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (_Disposed) return false;
                if (_Http != null) return _Http.IsConnected;
                if (_Stdio != null) return _Stdio.IsConnected;
                return false;
            }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _RpcErrorText = new Regex("^RPC Error (-?\\d+): (.*)$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex _StatusCodeText = new Regex("\\b([45]\\d\\d)\\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        private readonly SemaphoreSlim _CatalogLock = new SemaphoreSlim(1, 1);
        private McpHttpClient? _Http = null;
        private McpClient? _Stdio = null;
        private McpToolCatalog? _Catalog = null;
        private string? _InitializeJson = null;
        private string? _LastFailure = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private McpRemoteSession(McpTransportEnum transport)
        {
            Transport = transport;
        }

        /// <summary>
        /// Connect and complete the MCP handshake.
        /// </summary>
        /// <param name="options">Parsed resource settings.</param>
        /// <param name="resource">Resource (for headers built from its secrets).</param>
        /// <param name="connectorOptions">Connector options.</param>
        /// <param name="token">Cancellation token, including any timeout.</param>
        /// <returns>The connected session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>permission_denied</c> when Stdio is not allowed, <c>invalid_setting</c> for unusable settings, <c>auth_failed</c>, <c>not_found</c>, or <c>connection_failed</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public static async Task<McpRemoteSession> ConnectAsync(McpResourceOptions options, ConnectorResourceContext resource, McpConnectorOptions connectorOptions, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(connectorOptions);
            token.ThrowIfCancellationRequested();

            McpRemoteSession session = new McpRemoteSession(options.Transport);
            try
            {
                if (options.Transport == McpTransportEnum.Stdio) await session.ConnectStdioAsync(options, connectorOptions, token).ConfigureAwait(false);
                else await session.ConnectHttpAsync(options, resource, connectorOptions, token).ConfigureAwait(false);
                return session;
            }
            catch (Exception)
            {
                session.Dispose();
                throw;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ensure Stdio is allowed for a resource on this server, before any process is started.
        /// </summary>
        /// <param name="options">Parsed resource settings.</param>
        /// <param name="connectorOptions">Connector options.</param>
        /// <exception cref="ConnectorException">Thrown with <c>permission_denied</c> when either gate is closed.</exception>
        public static void EnsureStdioAllowed(McpResourceOptions options, McpConnectorOptions connectorOptions)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(connectorOptions);
            if (options.Transport != McpTransportEnum.Stdio) return;
            if (!connectorOptions.AllowStdio)
                throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, "Stdio MCP servers are disabled on this Hugo server (Connectors.McpAllowStdio is false). Use a Streamable HTTP server or ask the administrator.");
            if (!options.AllowStdio)
                throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, "This resource uses the Stdio transport but its allowStdio setting is false.");
        }

        /// <summary>
        /// Send a JSON-RPC request and return the result as JSON text.
        /// </summary>
        /// <param name="method">Method, for example <c>tools/list</c>.</param>
        /// <param name="parameters">Parameters, or null.</param>
        /// <param name="timeout">Timeout for this request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result JSON text (<c>null</c> when the server sent no result).</returns>
        /// <exception cref="McpRemoteException">Thrown when the server answers with a JSON-RPC error.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>connection_failed</c> when the session is closed.</exception>
        /// <exception cref="HttpRequestException">Thrown when an HTTP request fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown on cancellation or timeout.</exception>
        public async Task<string> CallAsync(string method, object? parameters, TimeSpan timeout, CancellationToken token)
        {
            if (String.IsNullOrEmpty(method)) throw new ArgumentNullException(nameof(method));
            if (_Disposed) throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The MCP session was closed.");
            token.ThrowIfCancellationRequested();
            int timeoutMs = (int)Math.Clamp(timeout.TotalMilliseconds, 1000, Int32.MaxValue);

            if (_Http != null)
            {
                JsonRpcResponse response = await _Http.CallAsync(method, parameters, timeoutMs, token).ConfigureAwait(false);
                if (response.Error != null) throw new McpRemoteException(response.Error.Code, response.Error.Message);
                return response.Result == null ? "null" : SerializeResult(response.Result);
            }

            if (_Stdio != null)
            {
                if (!_Stdio.IsConnected) throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The Stdio MCP server process is no longer running.");
                try
                {
                    JsonElement result = await _Stdio.CallAsync<JsonElement>(method, parameters, timeoutMs, token).ConfigureAwait(false);
                    return result.ValueKind == JsonValueKind.Undefined ? "null" : result.GetRawText();
                }
                catch (Exception e) when (e.GetType() == typeof(Exception) && _RpcErrorText.IsMatch(e.Message))
                {
                    Match m = _RpcErrorText.Match(e.Message);
                    int code = Int32.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : -32603;
                    throw new McpRemoteException(code, m.Groups[2].Value);
                }
            }

            throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The MCP session is not connected.");
        }

        /// <summary>
        /// The discovered tool list, fetched when missing, older than the refresh interval, or when forced.
        /// </summary>
        /// <param name="force">Fetch even when the cached list is fresh.</param>
        /// <param name="refreshInterval">Refresh interval.</param>
        /// <param name="maxPages">Most pages fetched.</param>
        /// <param name="timeout">Timeout per request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The list.</returns>
        /// <exception cref="McpRemoteException">Thrown when the server answers with a JSON-RPC error.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>upstream_error</c> when the list cannot be read.</exception>
        /// <exception cref="OperationCanceledException">Thrown on cancellation or timeout.</exception>
        public async Task<McpToolCatalog> GetCatalogAsync(bool force, TimeSpan refreshInterval, int maxPages, TimeSpan timeout, CancellationToken token)
        {
            McpToolCatalog? current = _Catalog;
            if (!force && current != null && !current.IsStale(refreshInterval, DateTime.UtcNow)) return current;

            await _CatalogLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                current = _Catalog;
                if (!force && current != null && !current.IsStale(refreshInterval, DateTime.UtcNow)) return current;

                List<McpRemoteTool> tools = new List<McpRemoteTool>();
                string? cursor = null;
                bool incomplete = false;
                for (int page = 0; ; page++)
                {
                    if (page >= Math.Max(1, maxPages))
                    {
                        incomplete = true;
                        break;
                    }
                    token.ThrowIfCancellationRequested();
                    string json = await CallAsync("tools/list", CursorParameters(cursor), timeout, token).ConfigureAwait(false);
                    McpWireListToolsResult result = AgentsJson.Read<McpWireListToolsResult>(json, "a tools/list result");
                    foreach (McpWireTool wire in result.Tools ?? new List<McpWireTool>())
                    {
                        if (wire == null || String.IsNullOrWhiteSpace(wire.Name)) continue;
                        tools.Add(new McpRemoteTool
                        {
                            Name = wire.Name,
                            Title = wire.Title ?? wire.Annotations?.Title,
                            Description = wire.Description ?? String.Empty,
                            InputSchemaJson = AgentsJson.Compact(wire.InputSchema),
                            ReadOnlyHint = wire.Annotations?.ReadOnlyHint,
                            DestructiveHint = wire.Annotations?.DestructiveHint,
                            IdempotentHint = wire.Annotations?.IdempotentHint,
                            OpenWorldHint = wire.Annotations?.OpenWorldHint
                        });
                    }
                    if (String.IsNullOrEmpty(result.NextCursor)) break;
                    cursor = result.NextCursor;
                }

                McpToolCatalog catalog = new McpToolCatalog(tools, DateTime.UtcNow, incomplete);
                _Catalog = catalog;
                return catalog;
            }
            finally
            {
                _CatalogLock.Release();
            }
        }

        /// <summary>
        /// Parameters for a paginated list request.
        /// </summary>
        /// <param name="cursor">Cursor, or null for the first page.</param>
        /// <returns>Parameters, or null.</returns>
        public static Dictionary<string, object?>? CursorParameters(string? cursor)
        {
            if (String.IsNullOrEmpty(cursor)) return null;
            return new Dictionary<string, object?>(StringComparer.Ordinal) { { "cursor", cursor } };
        }

        /// <summary>
        /// Map a failed connection message (from the client's log) to a connector error.
        /// </summary>
        /// <param name="endpoint">Endpoint description.</param>
        /// <param name="failure">Failure text, or null.</param>
        /// <returns>Exception to throw.</returns>
        public static ConnectorException MapConnectFailure(string endpoint, string? failure)
        {
            string detail = String.IsNullOrWhiteSpace(failure) ? "the handshake failed" : TextClip.Clip(TextClip.OneLine(failure), 300);
            Match m = _StatusCodeText.Match(failure ?? String.Empty);
            if (m.Success && (failure ?? String.Empty).Contains("status code", StringComparison.OrdinalIgnoreCase))
            {
                int status = Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (status == 401) return new ConnectorException(ConnectorErrorCodes.AuthFailed, "The MCP server at " + endpoint + " rejected the credentials (HTTP 401). Check the secret bound to the authorizationHeader slot.");
                if (status == 403) return new ConnectorException(ConnectorErrorCodes.PermissionDenied, "The MCP server at " + endpoint + " refused access (HTTP 403).");
                if (status == 404 || status == 405) return new ConnectorException(ConnectorErrorCodes.NotFound, "No MCP endpoint answered at " + endpoint + " (HTTP " + status.ToString(CultureInfo.InvariantCulture) + "). Check that url includes the MCP path, for example /mcp.");
                if (status == 429) return new ConnectorException(ConnectorErrorCodes.RateLimited, "The MCP server at " + endpoint + " is throttling requests (HTTP 429).");
                return new ConnectorException(ConnectorErrorCodes.UpstreamError, "The MCP server at " + endpoint + " failed the handshake (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ").");
            }
            return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Could not connect to the MCP server at " + endpoint + ": " + detail + ".");
        }

        /// <summary>
        /// End the remote session and release resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// End the remote session; for Stdio, stop the child process (input closed, then SIGTERM, then kill).
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                try
                {
                    _Http?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (HttpRequestException)
                {
                }
                try
                {
                    _Stdio?.Shutdown();
                    _Stdio?.Dispose();
                }
                catch (InvalidOperationException)
                {
                }
                _Http = null;
                _Stdio = null;
                _CatalogLock.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task ConnectHttpAsync(McpResourceOptions options, ConnectorResourceContext resource, McpConnectorOptions connectorOptions, CancellationToken token)
        {
            Uri url = options.RequireUrl();
            string baseUrl = url.GetLeftPart(UriPartial.Authority);
            string path = String.IsNullOrEmpty(url.PathAndQuery) ? "/" : url.PathAndQuery;

            McpHttpClient client = new McpHttpClient();
            _Http = client;
            client.ClientName = connectorOptions.ClientName;
            client.ClientVersion = connectorOptions.ClientVersion;
            client.PingIntervalMs = 0;
            client.AutoReconnectSse = false;
            client.RequestTimeoutMs = (int)Math.Clamp(options.Timeout.TotalMilliseconds, 1000, Int32.MaxValue);
            foreach (KeyValuePair<string, string> header in McpHeaders.Build(resource)) client.SetRequestHeader(header.Key, header.Value);
            client.Log += OnClientLog;
            client.ResponseReceived += OnResponseReceived;

            bool connected = await client.ConnectStreamableAsync(baseUrl, path, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!connected) throw MapConnectFailure(options.EndpointName(), _LastFailure);
            Server = new McpServerInfo(_InitializeJson == null ? null : AgentsJson.Read<McpWireInitializeResult>(_InitializeJson, "an initialize result"));
        }

        private async Task ConnectStdioAsync(McpResourceOptions options, McpConnectorOptions connectorOptions, CancellationToken token)
        {
            EnsureStdioAllowed(options, connectorOptions);
            if (String.IsNullOrWhiteSpace(options.Command))
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The Stdio transport needs the command setting (the executable that runs the MCP server).");

            McpClient client = new McpClient();
            _Stdio = client;
            client.ClientName = connectorOptions.ClientName;
            client.ClientVersion = connectorOptions.ClientVersion;
            client.PingIntervalMs = 0;
            client.InitializeTimeoutMs = (int)Math.Clamp(options.Timeout.TotalMilliseconds, 100, 600000);
            client.Log += OnClientLog;

            bool launched = await client.LaunchServerAsync(options.Command, options.Arguments.ToArray(), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!launched)
            {
                string detail = String.IsNullOrWhiteSpace(_LastFailure) ? "the process did not start or did not complete initialize" : TextClip.Clip(TextClip.OneLine(_LastFailure), 300);
                throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Could not start the Stdio MCP server '" + options.Command + "': " + detail + ".");
            }
            JsonElement? init = client.InitializeResult;
            Server = new McpServerInfo(init == null ? null : AgentsJson.Read<McpWireInitializeResult>(init.Value.GetRawText(), "an initialize result"));
        }

        private void OnClientLog(object? sender, string message)
        {
            if (String.IsNullOrEmpty(message)) return;
            if (message.Contains("failed", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Failed", StringComparison.Ordinal))
                _LastFailure = message;
        }

        private void OnResponseReceived(object? sender, ResponseReceivedEventArgs e)
        {
            if (e == null || !String.Equals(e.Method, "initialize", StringComparison.Ordinal)) return;
            if (e.Response?.Result == null) return;
            _InitializeJson = SerializeResult(e.Response.Result);
        }

        private static string SerializeResult(object result)
        {
            if (result is JsonElement element) return element.GetRawText();
            return JsonSerializer.Serialize(result, result.GetType());
        }

        #endregion
    }
}
