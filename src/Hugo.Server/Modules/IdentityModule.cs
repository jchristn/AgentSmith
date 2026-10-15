namespace Hugo.Server.Modules
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;

    /// <summary>
    /// Identity and administration: tenants, users, credentials, roles, permissions, assignments, sessions, audit,
    /// request history, settings, and server info. Publishes <see cref="IdentityService"/>,
    /// <see cref="ClusterSettingsService"/>, <see cref="SettingsService"/>, <see cref="RequestHistoryCaptureService"/>
    /// (also as <see cref="IRequestObserver"/>), and <see cref="RetentionService"/>.
    /// </summary>
    public class IdentityModule : IServerModule
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get
            {
                return "IdentityModule";
            }
        }

        #endregion

        #region Private-Members

        private ClusterSettingsService? _Cluster = null;
        private ObservabilityService? _Observability = null;
        private RequestHistoryCaptureService? _Capture = null;
        private RetentionService? _Retention = null;
        private SettingsService? _SettingsService = null;
        private CancellationTokenSource? _Stopping = null;
        private Task? _ClusterLoop = null;
        private Task? _RetentionLoop = null;
        private ServerContext? _Context = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task InitializeAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            _Context = context;
            // Any identity, role, or permission change clears the authentication cache along with the grants.
            context.Authorization.Invalidated += OnAuthorizationInvalidated;

            _Cluster = new ClusterSettingsService(context.Database, context.Settings.RequestHistory.Enabled, context.Settings.RequestHistory.RetentionDays);
            await _Cluster.RefreshAsync(token).ConfigureAwait(false);
            context.Register(_Cluster);
            ApplyClusterSettings();
            _Cluster.Changed += OnClusterSettingsChanged;

            _Observability = new ObservabilityService(_Cluster);
            context.Register(_Observability);

            _SettingsService = new SettingsService(context.Settings, _Cluster, context.Logging);
            context.Register(_SettingsService);
            context.Register(new IdentityService(context.Database, context.Cipher, context.Audit, context.Authorization, () => context.Settings.Auth.PasswordHashIterations));

            _Capture = new RequestHistoryCaptureService(context.Database, context.Settings.RequestHistory, _Cluster, context.Logging);
            context.Register(_Capture);
            context.Register<IRequestObserver>(_Capture);

            _Retention = new RetentionService(context.Database, _Cluster, context.Logging);
            context.Register(_Retention);
        }

        /// <inheritdoc />
        public void RegisterRoutes(ServerContext context, RouteRegistrar routes)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (routes == null) throw new ArgumentNullException(nameof(routes));

            IdentityService identity = context.Get<IdentityService>();
            new TenantRoutes(context, identity).Register(routes);
            new UserRoutes(context, identity).Register(routes);
            new CredentialRoutes(context, identity).Register(routes);
            new RoleRoutes(context).Register(routes);
            new PermissionRoutes(context).Register(routes);
            new AssignmentRoutes(context, identity).Register(routes);
            new SessionRoutes(context).Register(routes);
            new AuditRoutes(context).Register(routes);
            new RequestHistoryRoutes(context, context.Get<RequestHistoryCaptureService>()).Register(routes);
            new SettingsRoutes(context, context.Get<SettingsService>()).Register(routes);
            new ServerInfoRoutes(context, context.Get<RequestHistoryCaptureService>()).Register(routes);
            new ObservabilityRoutes(context.Get<ObservabilityService>()).Register(routes);
        }

        /// <inheritdoc />
        public Task StartAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _Stopping = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Capture!.Start();
            _ClusterLoop = Task.Run(() => _Cluster!.RunAsync(message => context.Logging.Warn("[Identity] " + message), _Stopping.Token));
            _RetentionLoop = Task.Run(() => _Retention!.RunAsync(_Stopping.Token));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken token)
        {
            if (_Cluster != null) _Cluster.Changed -= OnClusterSettingsChanged;
            if (_Context != null) _Context.Authorization.Invalidated -= OnAuthorizationInvalidated;
            if (_Capture != null) await _Capture.StopAsync(token).ConfigureAwait(false);
            if (_Stopping != null)
            {
                _Stopping.Cancel();
                try
                {
                    if (_ClusterLoop != null) await _ClusterLoop.ConfigureAwait(false);
                    if (_RetentionLoop != null) await _RetentionLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                _Stopping.Dispose();
                _Stopping = null;
            }
            _Capture?.Dispose();
            _SettingsService?.Dispose();
            _Observability?.Dispose();
        }

        #endregion

        #region Private-Methods

        private void OnAuthorizationInvalidated(object? sender, EventArgs e)
        {
            _Context?.Authentication.Cache.ClearLocal();
        }

        private void OnClusterSettingsChanged(object? sender, EventArgs e)
        {
            ApplyClusterSettings();
        }

        private void ApplyClusterSettings()
        {
            if (_Cluster == null || _Context == null) return;
            _Context.Authentication.Cache.CacheSeconds = _Cluster.GetInt("Auth.CacheSeconds");
        }

        #endregion
    }
}
