namespace Hugo.Server.Modules
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Inference;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Prompts;
    using Hugo.Core.Secrets;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;

    /// <summary>
    /// Hugo's integration layer: connectors (built-ins and plugins), resources, the tool invocation pipeline, approvals,
    /// tenant lockdown, resource knowledge, model endpoints, agent profiles, bindings, layered prompt templates, and the
    /// instruction compiler. Registers every service on the server context for the protocol surfaces, the routes, and the
    /// background jobs (approval expiry, model health sweep, idle session eviction). Runs after the configuration module,
    /// whose secret service it uses.
    /// </summary>
    public class IntegrationModule : IServerModule
    {
        #region Public-Members

        /// <summary>
        /// Module name.
        /// </summary>
        public string Name
        {
            get
            {
                return "IntegrationModule";
            }
        }

        /// <summary>
        /// Connectors registered in addition to the built-ins before startup (tests register the echo connector here).
        /// </summary>
        public List<Hugo.Connectors.Abstractions.IConnector> AdditionalConnectors { get; } = new List<Hugo.Connectors.Abstractions.IConnector>();

        #endregion

        #region Private-Members

        private ServerContext? _Context = null;
        private ConnectorRegistry? _Registry = null;
        private ConnectorSessionPool? _Pool = null;
        private PluginLoader? _Plugins = null;
        private ModelClientFactory? _ModelFactory = null;
        private ToolInvocationRecorder? _Recorder = null;
        private CancellationTokenSource? _Stopping = null;
        private readonly List<Task> _Loops = new List<Task>();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task InitializeAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _Context = context;
            Hugo.Server.Settings.HugoSettings settings = context.Settings;
            EventHub? events = context.TryGet<EventHub>();

            _Registry = new ConnectorRegistry();
            ConnectorRegistrations.RegisterBuiltIns(_Registry);
            foreach (Hugo.Connectors.Abstractions.IConnector connector in AdditionalConnectors) _Registry.Register(connector);
            context.Register(_Registry);

            _Plugins = new PluginLoader(context.Database, _Registry, context.Audit, context.Logging);
            context.Register(_Plugins);
            if (settings.Connectors.EnablePlugins)
            {
                string directory = Path.GetFullPath(settings.Connectors.PluginDirectory);
                List<PluginRecord> loaded = await _Plugins.LoadAllAsync(directory, token).ConfigureAwait(false);
                context.Logging.Info("[Integration] plugins in " + directory + ": " + loaded.Count + " found, " + loaded.FindAll(p => p.Loaded).Count + " loaded");
            }

            BuiltInTemplateCatalog builtIns = new BuiltInTemplateCatalog();
            builtIns.RegisterConnectors(_Registry.All());
            context.Register(builtIns);
            PromptTemplateService templates = new PromptTemplateService(context.Database, builtIns);
            int seeded = await templates.SeedGlobalDefaultsAsync(token).ConfigureAwait(false);
            if (seeded > 0) context.Logging.Info("[Integration] seeded or updated " + seeded + " Global prompt templates from the built-ins");
            context.Register(templates);

            _Pool = new ConnectorSessionPool();
            _Pool.IdleTimeout = TimeSpan.FromMinutes(settings.Connectors.SessionIdleMinutes);
            _Pool.MaxConcurrencyPerResource = () => settings.Connectors.MaxConcurrencyPerResource;
            context.Register(_Pool);

            SecretService secrets = context.Get<SecretService>();
            secrets.AddReferrerProvider(new ResourceSecretReferrerProvider(context.Database));

            ConnectorContextBuilder contexts = new ConnectorContextBuilder(context.Database, secrets, _Registry, _Pool, context.Audit, context.Logging);
            context.Register(contexts);

            _ModelFactory = new ModelClientFactory(context.Logging);
            ModelGateway gateway = new ModelGateway(_ModelFactory, new ModelGatewaySettings());
            context.Register(_ModelFactory);
            context.Register<IModelGateway>(gateway);
            ModelEndpointService models = new ModelEndpointService(context.Database, secrets, gateway, context.Audit, context.Logging, events);
            context.Register(models);

            ResourceService resources = new ResourceService(context.Database, _Registry, secrets, contexts, _Pool, context.Audit, events);
            resources.TestTimeoutSeconds = () => settings.Connectors.TestTimeoutSeconds;
            context.Register(resources);

            AgentProfileService profiles = new AgentProfileService(context.Database, context.Audit, events);
            context.Register(profiles);
            BindingService bindings = new BindingService(context.Database, _Registry, profiles, context.Audit, events);
            context.Register(bindings);
            ProfileResolver resolver = new ProfileResolver(context.Database, profiles);
            context.Register(resolver);

            ProfileBindingResolver bindingResolver = new ProfileBindingResolver(context.Database, _Registry);
            context.Register(bindingResolver);
            InstructionCompiler compiler = new InstructionCompiler(context.Database, bindingResolver, templates);
            compiler.MaxKnowledgeCharsPerResource = settings.Instructions.MaxKnowledgeCharsPerResource;
            compiler.CompactBindingThreshold = settings.Instructions.CompactBindingThreshold;
            compiler.MaxCacheEntries = settings.Instructions.CacheEntries;
            compiler.Warn = message => context.Logging.Warn("[Instructions] " + message);
            context.Register(compiler);
            ToolCatalogService catalog = new ToolCatalogService(context.Database, bindingResolver);
            context.Register(catalog);

            ApprovalService approvals = new ApprovalService(context.Database, context.Audit, events, context.Logging);
            approvals.ExpirySeconds = () => settings.Approvals.ExpirySeconds;
            approvals.AllowSelfApproval = () => settings.Approvals.AllowSelfApproval;
            context.Register(approvals);

            _Recorder = new ToolInvocationRecorder(context.Database, context.Logging);
            context.Register(_Recorder);
            ToolInvocationService invocations = new ToolInvocationService(context.Database, _Registry, context.Authorization, contexts, _Pool, approvals, models, _Recorder, settings, context.Logging, events);
            context.Register(invocations);
            context.Register(new LockdownService(context.Database, approvals, context.Audit, context.Authorization, events));

            // Any change that affects a profile's compiled instructions drops the tenant's cached compilations.
            resources.Changed += (sender, tenantId) => compiler.Invalidate(tenantId);
            profiles.Changed += (sender, tenantId) => compiler.Invalidate(tenantId);
            bindings.Changed += (sender, tenantId) => compiler.Invalidate(tenantId);
            models.Changed += (sender, tenantId) => compiler.Invalidate(tenantId);
            contexts.Changed += (sender, tenantId) => compiler.Invalidate(tenantId);

            IdentityService? identity = context.TryGet<IdentityService>();
            if (identity != null)
            {
                identity.TenantProvisioned = async (tenant, ct) => await profiles.EnsureDefaultAsync(tenant.Id, ct).ConfigureAwait(false);
                identity.ProfileExists = async (tenantId, profileId, ct) => await context.Database.AgentProfiles.ReadAsync(tenantId, profileId, ct).ConfigureAwait(false) != null;
            }

            await EnsureDefaultProfilesAsync(context, profiles, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void RegisterRoutes(ServerContext context, RouteRegistrar routes)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            new ConnectorRoutes(context.Get<ConnectorRegistry>(), context.TryGet<PluginLoader>()).Register(routes);
            new ResourceRoutes(context, context.Get<ResourceService>()).Register(routes);
            new AgentProfileRoutes(context, context.Get<AgentProfileService>(), context.Get<BindingService>(), context.Get<InstructionCompiler>(), context.Get<ToolCatalogService>()).Register(routes);
            new PromptLayerRoutes(context, context.Get<PromptTemplateService>()).Register(routes);
            new ModelEndpointRoutes(context, context.Get<ModelEndpointService>()).Register(routes);
            new ToolInvocationRoutes(context, context.Get<ToolInvocationService>(), context.Get<ProfileResolver>()).Register(routes);
            new ApprovalRoutes(context, context.Get<ApprovalService>()).Register(routes);
            new LockdownRoutes(context.Get<LockdownService>()).Register(routes);
        }

        /// <inheritdoc />
        public Task StartAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _Stopping = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationToken stopping = _Stopping.Token;
            _Recorder!.Start();
            ApprovalService approvals = context.Get<ApprovalService>();
            ModelEndpointService models = context.Get<ModelEndpointService>();
            _Loops.Add(Task.Run(() => approvals.RunAsync(stopping)));
            _Loops.Add(Task.Run(() => ModelHealthLoopAsync(context, models, stopping)));
            _Loops.Add(Task.Run(() => SessionSweepLoopAsync(stopping)));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken token)
        {
            if (_Stopping != null)
            {
                _Stopping.Cancel();
                foreach (Task loop in _Loops)
                {
                    try
                    {
                        await loop.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                _Loops.Clear();
                _Stopping.Dispose();
                _Stopping = null;
            }
            if (_Recorder != null)
            {
                await _Recorder.StopAsync(token).ConfigureAwait(false);
                _Recorder.Dispose();
            }
            _Pool?.Dispose();
            _Plugins?.Dispose();
            _ModelFactory?.Dispose();
            _Registry?.Dispose();
        }

        #endregion

        #region Private-Methods

        private static async Task EnsureDefaultProfilesAsync(ServerContext context, AgentProfileService profiles, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = 1000;
            while (true)
            {
                EnumerationResult<Tenant> page = await context.Database.Tenants.EnumerateAsync(query, token).ConfigureAwait(false);
                foreach (Tenant tenant in page.Objects)
                {
                    try
                    {
                        await profiles.EnsureDefaultAsync(tenant.Id, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        context.Logging.Warn("[Integration] provisioning the default profile of tenant " + tenant.Id + " failed: " + SecretScrubber.Shared.Scrub(e.Message));
                    }
                }
                if (page.EndOfResults || page.Objects.Count == 0) break;
                query.Skip = query.Skip + page.Objects.Count;
            }
        }

        private static async Task ModelHealthLoopAsync(ServerContext context, ModelEndpointService models, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int interval = context.Settings.Models.HealthIntervalSeconds;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval <= 0 ? 60 : interval), token).ConfigureAwait(false);
                    if (context.Settings.Models.HealthIntervalSeconds <= 0) continue;
                    await HugoTracing.BackgroundAsync("models", "health", async ct => await models.SweepAsync(ct).ConfigureAwait(false) > 0, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    context.Logging.Warn("[Models] health sweep failed: " + SecretScrubber.Shared.Scrub(e.Message));
                }
            }
        }

        private async Task SessionSweepLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
                    _Pool?.Sweep();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        #endregion
    }
}
