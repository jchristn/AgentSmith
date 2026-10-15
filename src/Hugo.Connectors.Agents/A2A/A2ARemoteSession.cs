namespace Hugo.Connectors.Agents.A2A
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.Mcp;
    using Voltaic.A2A;

    /// <summary>
    /// One remote A2A agent: its resolved card and a client for the interface Hugo talks to (JSON-RPC preferred, HTTP+JSON
    /// otherwise), sharing one <see cref="HttpClient"/> that carries the resource's bearer token. Pooled per resource
    /// configuration. Thread-safe.
    /// </summary>
    public class A2ARemoteSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The remote agent's card. Never null after connecting.
        /// </summary>
        public AgentCard Card
        {
            get
            {
                return _Card;
            }
        }

        /// <summary>
        /// When the card was resolved (UTC).
        /// </summary>
        public DateTime CardFetchedUtc
        {
            get
            {
                return _CardFetchedUtc;
            }
        }

        /// <summary>
        /// Endpoint URL of the interface in use.
        /// </summary>
        public string EndpointUrl { get; private set; } = String.Empty;

        /// <summary>
        /// Protocol binding of the interface in use (<c>JSONRPC</c> or <c>HTTP+JSON</c>).
        /// </summary>
        public string Binding { get; private set; } = String.Empty;

        /// <summary>
        /// Tenant the interface requires, or null.
        /// </summary>
        public string? Tenant { get; private set; } = null;

        /// <summary>
        /// Whether the card advertises streaming.
        /// </summary>
        public bool SupportsStreaming
        {
            get
            {
                return _Card.Capabilities?.Streaming == true;
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly Uri _CardUrl;
        private readonly bool _AllowCrossOrigin;
        private readonly SemaphoreSlim _CardLock = new SemaphoreSlim(1, 1);
        private AgentCard _Card = new AgentCard();
        private DateTime _CardFetchedUtc = DateTime.MinValue;
        private A2AClient? _JsonRpc = null;
        private A2AHttpJsonClient? _Rest = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private A2ARemoteSession(Uri cardUrl, string? bearerToken, bool allowCrossOrigin)
        {
            _CardUrl = cardUrl;
            _AllowCrossOrigin = allowCrossOrigin;
            _Http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            if (!String.IsNullOrWhiteSpace(bearerToken)) _Http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", McpHeaders.AuthorizationValue(bearerToken));
        }

        /// <summary>
        /// Resolve the card and prepare the client.
        /// </summary>
        /// <param name="options">Parsed resource settings.</param>
        /// <param name="resource">Resource (for the bearer token).</param>
        /// <param name="token">Cancellation token, including any timeout.</param>
        /// <returns>The session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c>, <c>not_supported</c>, <c>permission_denied</c>, or <c>upstream_error</c> when the card is unusable.</exception>
        /// <exception cref="HttpRequestException">Thrown when the card cannot be fetched.</exception>
        /// <exception cref="OperationCanceledException">Thrown on cancellation.</exception>
        public static async Task<A2ARemoteSession> ConnectAsync(A2AAgentResourceOptions options, ConnectorResourceContext resource, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(resource);
            Uri cardUrl = options.RequireCardUrl();
            string? bearer = resource.Secrets.Get(A2AAgentSettingKeys.BearerTokenSlot, A2AAgentSettingKeys.TokenField);
            A2ARemoteSession session = new A2ARemoteSession(cardUrl, bearer, options.AllowCrossOriginEndpoint);
            try
            {
                await session.RefreshCardAsync(token).ConfigureAwait(false);
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
        /// Fetch the card again and rebuild the client for its preferred interface.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The card.</returns>
        /// <exception cref="ConnectorException">Thrown when the card is unusable.</exception>
        /// <exception cref="HttpRequestException">Thrown when the card cannot be fetched.</exception>
        /// <exception cref="OperationCanceledException">Thrown on cancellation.</exception>
        public async Task<AgentCard> RefreshCardAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            await _CardLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AgentCard card = await FetchCardAsync(token).ConfigureAwait(false);
                AgentInterface chosen = ChooseInterface(card);
                Uri endpoint = ResolveEndpoint(chosen.Url);
                if (!_AllowCrossOrigin && !SameOrigin(endpoint, _CardUrl))
                    throw new ConnectorException(
                        ConnectorErrorCodes.PermissionDenied,
                        "The agent card points its endpoint at " + endpoint.GetLeftPart(UriPartial.Authority) + ", a different origin than agentCardUrl ("
                        + _CardUrl.GetLeftPart(UriPartial.Authority) + "). Hugo does not send the bearer token there unless allowCrossOriginEndpoint is true on the resource.");

                _JsonRpc?.Dispose();
                _Rest?.Dispose();
                _JsonRpc = null;
                _Rest = null;
                if (IsJsonRpc(chosen.ProtocolBinding)) _JsonRpc = new A2AClient(endpoint, _Http);
                else _Rest = new A2AHttpJsonClient(endpoint, _Http);

                EndpointUrl = endpoint.AbsoluteUri;
                Binding = IsJsonRpc(chosen.ProtocolBinding) ? "JSONRPC" : "HTTP+JSON";
                Tenant = String.IsNullOrWhiteSpace(chosen.Tenant) ? null : chosen.Tenant;
                _Card = card;
                _CardFetchedUtc = DateTime.UtcNow;
                return card;
            }
            finally
            {
                _CardLock.Release();
            }
        }

        /// <summary>
        /// Send a message and wait for the agent's first response (a task or a message).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        /// <exception cref="A2AProtocolException">Thrown when the agent returns an A2A error.</exception>
        /// <exception cref="HttpRequestException">Thrown when the request fails.</exception>
        public Task<SendMessageResponse> SendAsync(SendMessageRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Tenant ??= Tenant;
            if (_JsonRpc != null) return _JsonRpc.SendMessageAsync(request, token);
            if (_Rest != null) return _Rest.SendMessageAsync(request, token);
            throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The A2A session is not connected.");
        }

        /// <summary>
        /// Send a message as a stream of task, status, artifact, and message events.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        /// <exception cref="ConnectorException">Thrown when the session is not connected.</exception>
        public IAsyncEnumerable<StreamResponse> StreamAsync(SendMessageRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Tenant ??= Tenant;
            if (_JsonRpc != null) return _JsonRpc.SendStreamingMessageAsync(request, token);
            if (_Rest != null) return _Rest.SendStreamingMessageAsync(request, token);
            throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The A2A session is not connected.");
        }

        /// <summary>
        /// Read a task.
        /// </summary>
        /// <param name="taskId">Task identifier.</param>
        /// <param name="historyLength">Most history messages returned, or null for the agent's default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="A2AProtocolException">Thrown when the agent returns an A2A error (for example task not found).</exception>
        public Task<AgentTask> GetTaskAsync(string taskId, int? historyLength, CancellationToken token)
        {
            if (String.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));
            GetTaskRequest request = new GetTaskRequest { Id = taskId, HistoryLength = historyLength, Tenant = Tenant };
            if (_JsonRpc != null) return _JsonRpc.GetTaskAsync(request, token);
            if (_Rest != null) return _Rest.GetTaskAsync(request, token);
            throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The A2A session is not connected.");
        }

        /// <summary>
        /// Release the clients.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Release the clients.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _JsonRpc?.Dispose();
                _Rest?.Dispose();
                _Http.Dispose();
                _CardLock.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<AgentCard> FetchCardAsync(CancellationToken token)
        {
            string url = _CardUrl.GetLeftPart(UriPartial.Path);
            if (url.EndsWith(A2AProtocol.AgentCardPath, StringComparison.Ordinal) || !_CardUrl.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string baseUrl = url.EndsWith(A2AProtocol.AgentCardPath, StringComparison.Ordinal) ? url.Substring(0, url.Length - A2AProtocol.AgentCardPath.Length) : url.TrimEnd('/');
                using (A2ACardResolver resolver = new A2ACardResolver(_Http))
                {
                    try
                    {
                        return await resolver.GetAgentCardAsync(baseUrl, token).ConfigureAwait(false);
                    }
                    catch (JsonException e)
                    {
                        throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The agent card at " + _CardUrl.AbsoluteUri + " is not valid JSON: " + e.Message);
                    }
                    catch (A2AProtocolException e)
                    {
                        throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The response at " + _CardUrl.AbsoluteUri + " is not an agent card: " + e.Message);
                    }
                }
            }

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _CardUrl))
            {
                request.Headers.TryAddWithoutValidation(A2AProtocol.VersionHeader, A2AProtocol.ProtocolVersion);
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    try
                    {
                        AgentCard? card = JsonSerializer.Deserialize<AgentCard>(json, A2AJson.DefaultOptions);
                        if (card == null) throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The response at " + _CardUrl.AbsoluteUri + " is not an agent card.");
                        return card;
                    }
                    catch (JsonException e)
                    {
                        throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The agent card at " + _CardUrl.AbsoluteUri + " is not valid JSON: " + e.Message);
                    }
                }
            }
        }

        private static AgentInterface ChooseInterface(AgentCard card)
        {
            List<AgentInterface> interfaces = (card.SupportedInterfaces ?? new List<AgentInterface>()).Where(i => i != null && !String.IsNullOrWhiteSpace(i.Url)).ToList();
            AgentInterface? jsonRpc = interfaces.FirstOrDefault(i => IsJsonRpc(i.ProtocolBinding));
            if (jsonRpc != null) return jsonRpc;
            AgentInterface? rest = interfaces.FirstOrDefault(i => String.Equals(i.ProtocolBinding, "HTTP+JSON", StringComparison.OrdinalIgnoreCase));
            if (rest != null) return rest;
            throw new ConnectorException(ConnectorErrorCodes.NotSupported, "The agent card of '" + TextClip.OneLine(card.Name) + "' lists no JSON-RPC or HTTP+JSON interface; Hugo cannot reach it (gRPC is not supported).");
        }

        private Uri ResolveEndpoint(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)) return absolute;
            if (Uri.TryCreate(_CardUrl, url, out Uri? relative)) return relative;
            throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The agent card lists an endpoint URL Hugo cannot use: " + TextClip.Clip(url, 200));
        }

        private static bool IsJsonRpc(string? binding)
        {
            return String.Equals(binding, "JSONRPC", StringComparison.OrdinalIgnoreCase) || String.Equals(binding, "JSON-RPC", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameOrigin(Uri a, Uri b)
        {
            return String.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
                && String.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
                && a.Port == b.Port;
        }

        #endregion
    }
}
