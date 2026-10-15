namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Profiles;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Server.Services;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Hugo's MCP server (P7.1 to P7.10, D-005), adapted from Garrison's GarrisonMcpServer. Voltaic's
    /// <see cref="McpHttpServer"/> listens on an internal loopback port behind <see cref="McpProxy"/>. Because Voltaic
    /// answers initialize, server/discover, tools/list, and resources/list the same way for every session, Hugo answers
    /// them per caller: compiled instructions (surface Mcp) for the caller's profile, the caller's tool catalog (D-017),
    /// and the caller's resources. Every connector tool, meta tool, and management tool is registered once with a
    /// permissive schema (arguments are validated by the pipeline, so a mistake is an isError result) and dispatched
    /// through <see cref="McpToolExecutor"/> after the <see cref="McpToolAuthorizationRegistry"/> check. Thread-safe.
    /// </summary>
    public class HugoMcpServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Path of the internal endpoint.
        /// </summary>
        public static readonly string InternalPath = "/mcp";

        /// <summary>
        /// URI of the compiled instructions resource.
        /// </summary>
        public static readonly string InstructionsUri = "hugo://profile/instructions";

        /// <summary>
        /// URI of the pending approvals resource.
        /// </summary>
        public static readonly string PendingApprovalsUri = "hugo://approvals/pending";

        /// <summary>
        /// URI template of a bound resource's guidance.
        /// </summary>
        public static readonly string ResourceUriTemplate = "hugo://resources/{alias}";

        /// <summary>
        /// Tool authorization registry.
        /// </summary>
        public McpToolAuthorizationRegistry Registry { get; }

        /// <summary>
        /// Tool executor.
        /// </summary>
        public McpToolExecutor Executor { get; }

        /// <summary>
        /// The public endpoint, or null before start or when MCP is disabled.
        /// </summary>
        public McpProxy? Proxy
        {
            get
            {
                return _Proxy;
            }
        }

        /// <summary>
        /// Loopback port of the internal endpoint, or 0 before start.
        /// </summary>
        public int InternalPort
        {
            get
            {
                return _InternalPort;
            }
        }

        /// <summary>
        /// Requests in flight through the proxy.
        /// </summary>
        public int ActiveCallers
        {
            get
            {
                return _Callers.Count;
            }
        }

        #endregion

        #region Private-Members

        private static readonly string _HandleClaim = "handle";
        private static readonly string _PermissiveSchema = "{\"type\":\"object\"}";
        private readonly ServerContext _Context;
        private readonly ConcurrentDictionary<string, McpCaller> _Callers = new ConcurrentDictionary<string, McpCaller>(StringComparer.Ordinal);
        private readonly HashSet<string> _RegisteredTools = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _ToolLock = new object();
        private readonly string _Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private readonly string _Header = "[Mcp] ";
        private McpHttpServer? _Server = null;
        private CancellationTokenSource? _Cts = null;
        private Task? _ServerTask = null;
        private McpProxy? _Proxy = null;
        private int _InternalPort = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public HugoMcpServer(ServerContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            Registry = new McpToolAuthorizationRegistry(context.Scopes);
            Executor = new McpToolExecutor(context, Registry);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Map every tool in the authorization registry (connector tools from the loaded connectors, plus the built-in
        /// tools) and register the meta tool provider with the tool catalog. Call after the integration module has
        /// initialized.
        /// </summary>
        public void Initialize()
        {
            ConnectorRegistry connectors = _Context.Get<ConnectorRegistry>();
            foreach (IConnector connector in connectors.All())
            {
                foreach (IConnectorTool tool in connector.Tools)
                {
                    McpToolAuthorization entry = new McpToolAuthorization();
                    entry.Name = tool.Descriptor.Name;
                    entry.Kind = McpToolKindEnum.Connector;
                    entry.ResourceType = ResourceTypeEnum.AgentProfile;
                    entry.Operation = OperationTypeEnum.Execute;
                    entry.Summary = String.IsNullOrEmpty(tool.Descriptor.Title) ? tool.Descriptor.Name : tool.Descriptor.Title;
                    Registry.Map(entry);
                }
            }
            foreach (McpToolDefinition definition in McpToolCatalog.All()) Registry.Map(definition.Authorization());
            _Context.Get<ToolCatalogService>().AddMetaToolProvider(new McpMetaToolProvider(_Context.Get<ProfileBindingResolver>(), Executor));
        }

        /// <summary>
        /// Start the internal endpoint and create the proxy.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the internal endpoint fails to start.</exception>
        public async Task StartAsync(CancellationToken token)
        {
            if (!_Context.Settings.Mcp.Enable) return;
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _InternalPort = _Context.Settings.Mcp.InternalPort > 0 ? _Context.Settings.Mcp.InternalPort : FreeLoopbackPort();
            McpHttpServer server = new McpHttpServer("127.0.0.1", _InternalPort, "/rpc", "/events", false, InternalPath);
            server.EnableLegacyEndpoints = false;
            server.SessionTimeoutSeconds = Math.Max(10, _Context.Settings.Mcp.SessionIdleTimeoutSeconds);
            server.ClientIdentifier = ProxiedClient;
            server.ServerName = "Hugo";
            server.ServerVersion = Constants.ProductVersion;
            server.MaxMessageSize = _Context.Settings.Mcp.MaxMessageSize;
            server.EnableCors = false;
            server.OriginPolicy.OriginValidator = origin => true;
            server.AuthenticationHandler = AuthenticateAsync;
            server.Log += (_, message) => _Context.Logging.Debug(_Header + message);

            // Voltaic answers these the same way for every session; Hugo answers them per caller.
            server.RegisterMethod("initialize", (RpcParameters? args, RpcCallContext? call, CancellationToken ct) => InitializeAsync(args, call, ct));
            server.RegisterMethod("server/discover", (RpcParameters? args, RpcCallContext? call, CancellationToken ct) => DiscoverAsync(call, ct));
            server.RegisterMethod("tools/list", (RpcParameters? args, RpcCallContext? call, CancellationToken ct) => ListToolsAsync(call, ct));
            server.RegisterMethod("resources/list", (RpcParameters? args, RpcCallContext? call, CancellationToken ct) => ListResourcesAsync(call, ct));
            _Server = server;

            foreach (McpToolAuthorization entry in Registry.All()) RegisterTool(entry.Name, entry.Summary);
            RegisterPrompts(server);
            RegisterResources(server);

            _ServerTask = server.StartAsync(_Cts.Token);
            await Task.Delay(100, token).ConfigureAwait(false);
            if (_ServerTask.IsFaulted) throw new InvalidOperationException("The MCP server failed to start on internal port " + _InternalPort + ": " + _ServerTask.Exception?.GetBaseException().Message);
            _Proxy = new McpProxy(_Context, this, _InternalPort, InternalPath, _Secret);
            _Context.Logging.Info(_Header + "serving /mcp on the REST port (internal endpoint 127.0.0.1:" + _InternalPort + ", " + Registry.Count + " tools mapped)");
        }

        /// <summary>
        /// Stop the internal endpoint.
        /// </summary>
        public void Stop()
        {
            try
            {
                _Cts?.Cancel();
                _Server?.Stop();
            }
            catch (Exception e)
            {
                _Context.Logging.Warn(_Header + "error stopping: " + e.Message);
            }
        }

        /// <summary>
        /// Hand a caller to the internal endpoint for one request.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <returns>Handle to send to the internal endpoint.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        public string RegisterCaller(McpCaller caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            string handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _Callers[handle] = caller;
            return handle;
        }

        /// <summary>
        /// Forget a caller handle once its request has been answered.
        /// </summary>
        /// <param name="handle">Handle.</param>
        public void ReleaseCaller(string handle)
        {
            if (!String.IsNullOrEmpty(handle)) _Callers.TryRemove(handle, out McpCaller? _);
        }

        /// <summary>
        /// The caller of the current internal request.
        /// </summary>
        /// <param name="call">Call context, or null.</param>
        /// <returns>Caller or null.</returns>
        public McpCaller? Caller(RpcCallContext? call)
        {
            if (call == null || !call.Claims.TryGetValue(_HandleClaim, out string? handle) || String.IsNullOrEmpty(handle)) return null;
            _Callers.TryGetValue(handle, out McpCaller? caller);
            return caller;
        }

        /// <summary>
        /// Before a tools/call is relayed: Voltaic answers tools/call only for registered tools, so a tool the caller's
        /// catalog offers that is not registered yet (a meta tool from another provider) is registered here; the
        /// dispatcher refuses it unless it is mapped in the authorization registry.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="name">Tool name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task PrepareToolCallAsync(McpCaller caller, string? name, CancellationToken token)
        {
            if (caller == null || String.IsNullOrWhiteSpace(name) || name.Length > 128) return;
            lock (_ToolLock)
            {
                if (_RegisteredTools.Contains(name)) return;
            }
            CallerToolCatalog catalog = await _Context.Get<ToolCatalogService>().ListAsync(caller.Principal, caller.TenantId, caller.Profile.Id, McpMetaToolProvider.Surface, token).ConfigureAwait(false);
            CallerTool? offered = catalog.Tools.Concat(catalog.MetaTools).FirstOrDefault(t => String.Equals(t.Name, name, StringComparison.Ordinal));
            if (offered == null) return;
            RegisterTool(offered.Name, String.IsNullOrEmpty(offered.Title) ? offered.Name : offered.Title);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Stop();
                _Proxy?.Dispose();
                _Server?.Dispose();
                _Cts?.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void RegisterTool(string name, string description)
        {
            McpHttpServer? server = _Server;
            if (server == null) return;
            lock (_ToolLock)
            {
                if (!_RegisteredTools.Add(name)) return;
            }
            using (JsonDocument schema = JsonDocument.Parse(_PermissiveSchema))
            {
                server.RegisterTool(name, String.IsNullOrWhiteSpace(description) ? name : description, schema.RootElement.Clone(),
                    (RpcParameters? args, RpcCallContext? call, CancellationToken ct) => DispatchAsync(name, args, call, ct));
            }
        }

        private async Task<object> DispatchAsync(string name, RpcParameters? args, RpcCallContext? call, CancellationToken token)
        {
            return await Executor.ExecuteAsync(Caller(call), name, args, token).ConfigureAwait(false);
        }

        private McpCaller RequireCaller(RpcCallContext? call)
        {
            return Caller(call) ?? throw new McpProtocolException(-32001, "Unauthorized: the request carried no authenticated caller.");
        }

        private async Task<object> InitializeAsync(RpcParameters? args, RpcCallContext? call, CancellationToken token)
        {
            McpCaller caller = RequireCaller(call);
            McpInitializeParams? request = null;
            try
            {
                request = String.IsNullOrWhiteSpace(args?.RawJson) ? null : Serializer.Deserialize<McpInitializeParams>(args!.RawJson);
            }
            catch (JsonException)
            {
                request = null;
            }
            if (request == null || String.IsNullOrWhiteSpace(request.ProtocolVersion)) throw McpProtocolException.InvalidParams("initialize requires a protocolVersion string.");
            if (!request.Capabilities.HasValue || request.Capabilities.Value.ValueKind != JsonValueKind.Object) throw McpProtocolException.InvalidParams("initialize requires a capabilities object.");
            if (request.ClientInfo == null || request.ClientInfo.Name == null || request.ClientInfo.Version == null) throw McpProtocolException.InvalidParams("initialize requires a clientInfo object with name and version strings.");

            string maximum = _Server!.MaximumHandshakeProtocolVersion;
            string negotiated = McpProtocol.IsSupportedVersion(request.ProtocolVersion)
                ? McpProtocol.NegotiateHandshakeVersion(request.ProtocolVersion, maximum)
                : maximum;
            Dictionary<string, object?> result = new Dictionary<string, object?>(StringComparer.Ordinal);
            result["protocolVersion"] = negotiated;
            result["capabilities"] = Capabilities();
            result["serverInfo"] = new McpImplementation { Name = "Hugo", Version = Constants.ProductVersion };
            result["instructions"] = await InstructionsAsync(caller, token).ConfigureAwait(false);
            return result;
        }

        private async Task<object> DiscoverAsync(RpcCallContext? call, CancellationToken token)
        {
            McpCaller caller = RequireCaller(call);
            McpProtocolVersionInfo? cap = McpProtocol.GetVersionInfo(_Server!.MaximumHandshakeProtocolVersion);
            List<McpProtocolVersionInfo> all = McpProtocol.SupportedVersions.ToList();
            int capIndex = cap == null ? -1 : all.IndexOf(cap);
            McpDiscoverResult result = new McpDiscoverResult();
            result.SupportedVersions = all.Where((info, index) => info.Era == McpProtocolEra.Stateless || index <= capIndex).Select(info => info.Version).ToList();
            result.Capabilities = Capabilities();
            result.Instructions = await InstructionsAsync(caller, token).ConfigureAwait(false);
            result.Meta = new Dictionary<string, object?> { { McpProtocol.MetaServerInfoKey, new McpImplementation { Name = "Hugo", Version = Constants.ProductVersion } } };
            return result;
        }

        private static McpServerCapabilities Capabilities()
        {
            McpServerCapabilities capabilities = new McpServerCapabilities();
            capabilities.Tools = new McpListChangedCapability { ListChanged = true };
            capabilities.Resources = new McpResourceCapability { ListChanged = true, Subscribe = true };
            capabilities.Prompts = new McpListChangedCapability { ListChanged = true };
            capabilities.Completions = new Dictionary<string, object>();
            capabilities.Logging = new Dictionary<string, object>();
            return capabilities;
        }

        private async Task<string> InstructionsAsync(McpCaller caller, CancellationToken token)
        {
            CompiledInstructions compiled = await _Context.Get<InstructionCompiler>().CompileAsync(caller.TenantId, caller.Profile.Id, caller.Principal, InvocationSurfaceEnum.Mcp, token).ConfigureAwait(false);
            return compiled.Text;
        }

        private async Task<object> ListToolsAsync(RpcCallContext? call, CancellationToken token)
        {
            McpCaller caller = RequireCaller(call);
            CallerToolCatalog catalog = await _Context.Get<ToolCatalogService>().ListAsync(caller.Principal, caller.TenantId, caller.Profile.Id, McpMetaToolProvider.Surface, token).ConfigureAwait(false);
            McpListToolsResult result = new McpListToolsResult();
            foreach (CallerTool tool in catalog.MetaTools.Concat(catalog.Tools))
            {
                // Fail closed: a tool without an authorization mapping is never listed.
                if (!Registry.IsMapped(tool.Name)) continue;
                result.Tools.Add(Definition(tool));
            }
            result.CacheScope = "private";
            return result;
        }

        private static ToolDefinition Definition(CallerTool tool)
        {
            ToolDefinition definition = new ToolDefinition();
            definition.Name = tool.Name;
            definition.Title = String.IsNullOrEmpty(tool.Title) || tool.Title == tool.Name ? null : tool.Title;
            definition.Description = String.IsNullOrWhiteSpace(tool.Description) ? tool.Name : tool.Description;
            using (JsonDocument schema = JsonDocument.Parse(String.IsNullOrWhiteSpace(tool.InputSchemaJson) ? _PermissiveSchema : tool.InputSchemaJson))
            {
                definition.InputSchema = schema.RootElement.Clone();
            }
            McpAnnotations annotations = new McpAnnotations();
            annotations.ReadOnlyHint = tool.KnownEffect == EffectEnum.Read;
            annotations.DestructiveHint = !tool.IsMetaTool && tool.BaseEffect == EffectEnum.Destructive;
            annotations.OpenWorldHint = !tool.IsMetaTool;
            definition.Annotations = annotations;
            return definition;
        }

        private async Task<object> ListResourcesAsync(RpcCallContext? call, CancellationToken token)
        {
            McpCaller caller = RequireCaller(call);
            McpListResourcesResult result = new McpListResourcesResult();
            result.Resources.Add(new McpResource { Uri = InstructionsUri, Name = "instructions", Title = "Compiled instructions of profile " + caller.Profile.Name, MimeType = "text/markdown" });
            result.Resources.Add(new McpResource { Uri = PendingApprovalsUri, Name = "pending-approvals", Title = "Your pending approvals", MimeType = "application/json" });
            foreach (McpBoundResource bound in await Executor.BoundAsync(caller, token).ConfigureAwait(false))
            {
                McpResource resource = new McpResource();
                resource.Uri = "hugo://resources/" + Uri.EscapeDataString(bound.Alias);
                resource.Name = bound.Alias;
                resource.Title = bound.Alias + " (" + bound.Connector + ", " + bound.AccessMode + ")";
                resource.Description = bound.Description ?? ("Guidance, notes, and tools for " + bound.Alias + ".");
                resource.MimeType = "text/markdown";
                result.Resources.Add(resource);
            }
            result.CacheScope = "private";
            return result;
        }

        private void RegisterPrompts(McpHttpServer server)
        {
            server.RegisterPrompt(new McpPrompt { Name = "hugo_briefing", Title = "Briefing", Description = "The compiled instructions of your agent profile: persona, safety rules, and every bound resource." },
                async (RpcParameters? args, CancellationToken ct) =>
                {
                    McpCaller caller = RequireCaller(RpcCallContext.Current);
                    return Prompt("Briefing for profile " + caller.Profile.Name, await InstructionsAsync(caller, ct).ConfigureAwait(false));
                });

            McpPromptArgument symptom = new McpPromptArgument { Name = "symptom", Description = "What is wrong (for example \"checkout is slow\").", Required = true };
            server.RegisterPrompt(new McpPrompt { Name = "hugo_investigate", Title = "Investigate", Description = "Investigate a symptom across every bound resource.", Arguments = new List<McpPromptArgument> { symptom } },
                async (RpcParameters? args, CancellationToken ct) =>
                {
                    McpCaller caller = RequireCaller(RpcCallContext.Current);
                    McpInvestigateArguments? arguments = String.IsNullOrWhiteSpace(args?.RawJson) ? null : Serializer.Deserialize<McpInvestigateArguments>(args!.RawJson);
                    if (arguments == null || String.IsNullOrWhiteSpace(arguments.Symptom)) throw McpProtocolException.InvalidParams("hugo_investigate requires the symptom argument.");
                    List<McpBoundResource> bound = await Executor.BoundAsync(caller, ct).ConfigureAwait(false);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("Investigate ").Append(arguments.Symptom.Trim()).Append(" across my stack.\n\n");
                    sb.Append("Resources you can use: ").Append(bound.Count == 0 ? "none" : String.Join(", ", bound.Select(b => b.Alias + " (" + b.Connector + ", " + b.AccessMode + ")"))).Append(".\n\n");
                    sb.Append("Start with read-only checks: recent errors in the logs, the health and load of the hosts and databases involved, and anything that changed recently. ");
                    sb.Append("Call hugo_describe_resource for a resource's full guidance before you use it. Correlate what you find by time, name the most likely cause with the evidence, and propose a fix. ");
                    sb.Append("Do not make changes unless I ask; any Write or Destructive call may need my approval.");
                    return Prompt("Investigate " + arguments.Symptom.Trim(), sb.ToString());
                });

            server.RegisterPrompt(new McpPrompt { Name = "hugo_health_sweep", Title = "Health sweep", Description = "Check the health of every bound resource." },
                async (RpcParameters? args, CancellationToken ct) =>
                {
                    McpCaller caller = RequireCaller(RpcCallContext.Current);
                    List<McpBoundResource> bound = await Executor.BoundAsync(caller, ct).ConfigureAwait(false);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("Check the health of every bound resource");
                    if (bound.Count == 0) sb.Append(". No resources are bound to profile ").Append(caller.Profile.Name).Append('.');
                    else
                    {
                        sb.Append(":\n");
                        foreach (McpBoundResource b in bound) sb.Append("- ").Append(b.Alias).Append(" (").Append(b.Connector).Append(")\n");
                        sb.Append("\nFor each one, run the cheapest read that proves it is reachable and healthy (system info, server info, a table list, a log tail, a metric), using only Read tools. ");
                        sb.Append("Report a table with resource, status (healthy, degraded, unreachable), and the evidence, then list anything that needs attention.");
                    }
                    return Prompt("Health sweep", sb.ToString());
                });
        }

        private static McpGetPromptResult Prompt(string description, string text)
        {
            McpGetPromptResult result = new McpGetPromptResult();
            result.Description = description;
            result.Messages.Add(new McpPromptMessage { Role = "user", Content = new McpTextContent { Text = text } });
            return result;
        }

        private void RegisterResources(McpHttpServer server)
        {
            server.RegisterResource(new McpResource { Uri = InstructionsUri, Name = "instructions", Title = "Compiled instructions", MimeType = "text/markdown" },
                async (string uri, CancellationToken ct) =>
                {
                    McpCaller caller = RequireCaller(RpcCallContext.Current);
                    return Read(uri, "text/markdown", await InstructionsAsync(caller, ct).ConfigureAwait(false));
                });
            server.RegisterResource(new McpResource { Uri = PendingApprovalsUri, Name = "pending-approvals", Title = "Your pending approvals", MimeType = "application/json" },
                async (string uri, CancellationToken ct) =>
                {
                    McpCaller caller = RequireCaller(RpcCallContext.Current);
                    McpApprovalList list = await Executor.ApprovalsAsync(caller, ApprovalStatusEnum.Pending, 100, ct).ConfigureAwait(false);
                    return Read(uri, "application/json", Serializer.SerializeReadable(list));
                });
            server.RegisterResourceTemplate(new McpResourceTemplate { UriTemplate = ResourceUriTemplate, Name = "resource", Title = "Bound resource guidance", Description = "Full guidance, notes, knowledge, and tools for one bound resource.", MimeType = "text/markdown" },
                async (string uri, IReadOnlyDictionary<string, string> variables, CancellationToken ct) =>
                {
                    McpCaller caller = RequireCaller(RpcCallContext.Current);
                    variables.TryGetValue("alias", out string? alias);
                    ResourceInstructions described;
                    try
                    {
                        described = await Executor.DescribeAsync(caller, alias == null ? null : Uri.UnescapeDataString(alias), ct).ConfigureAwait(false);
                    }
                    catch (McpToolErrorException e)
                    {
                        throw new McpProtocolException(-32002, "Resource not found: " + e.Message);
                    }
                    return Read(uri, "text/markdown", McpToolExecutor.DescribeText(described));
                });
        }

        private static McpReadResourceResult Read(string uri, string mimeType, string text)
        {
            McpReadResourceResult result = new McpReadResourceResult();
            result.Contents.Add(new McpTextResourceContents { Uri = uri, MimeType = mimeType, Text = text });
            result.CacheScope = "private";
            return result;
        }

        private Task<AuthenticationResult> AuthenticateAsync(HttpListenerRequest request)
        {
            // Requests relayed by the proxy were authenticated there once; the per-process secret proves the headers came
            // from it. Nothing else may reach the internal endpoint.
            string? secret = request.Headers[McpProxy.ProxySecretHeader];
            string? handle = request.Headers[McpProxy.ProxyHandleHeader];
            string? principal = request.Headers[McpProxy.ProxyPrincipalHeader];
            if (secret == null || !PasswordHasher.FixedTimeEquals(secret, _Secret) || String.IsNullOrEmpty(handle) || String.IsNullOrEmpty(principal) || !_Callers.ContainsKey(handle))
                return Task.FromResult(AuthenticationResult.BearerChallenge(null, "invalid_token", "Authentication is required."));
            AuthenticationResult result = new AuthenticationResult();
            result.IsAuthenticated = true;
            result.Principal = principal;
            result.Claims = new Dictionary<string, string> { { _HandleClaim, handle } };
            return Task.FromResult(result);
        }

        private string? ProxiedClient(HttpListenerRequest request)
        {
            string? secret = request.Headers[McpProxy.ProxySecretHeader];
            if (secret == null || !PasswordHasher.FixedTimeEquals(secret, _Secret)) return null;
            return request.Headers[McpProxy.ProxyClientHeader];
        }

        private static int FreeLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        #endregion
    }
}
