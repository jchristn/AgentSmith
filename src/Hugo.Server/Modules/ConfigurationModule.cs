namespace Hugo.Server.Modules
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Secrets;
    using Hugo.Server.Routes;

    /// <summary>
    /// Configuration domain for Phases 1 and 2: the secrets vault, master key rotation, and prompt templates. Publishes
    /// <see cref="SecretService"/> and <see cref="MasterKeyRotationService"/>. Later phases add resources, agent
    /// profiles, bindings, and model endpoints here.
    /// </summary>
    public class ConfigurationModule : IServerModule
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get
            {
                return "ConfigurationModule";
            }
        }

        #endregion

        #region Private-Members

        private MasterKeyRotationService? _Rotation = null;
        private ServerContext? _Context = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task InitializeAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _Context = context;
            context.Register(new SecretService(context.Database, context.Cipher));
            _Rotation = new MasterKeyRotationService(context.Database, context.Cipher);
            _Rotation.BatchSize = context.Settings.Secrets.RotationBatchSize;
            _Rotation.Progress += OnRotationProgress;
            context.Register(_Rotation);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void RegisterRoutes(ServerContext context, RouteRegistrar routes)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (routes == null) throw new ArgumentNullException(nameof(routes));

            new SecretRoutes(context, context.Get<SecretService>()).Register(routes);
            new SystemRoutes(context, context.Get<MasterKeyRotationService>()).Register(routes);
            new PromptTemplateRoutes(context).Register(routes);
        }

        /// <inheritdoc />
        public Task StartAsync(ServerContext context, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken token)
        {
            if (_Rotation != null) _Rotation.Progress -= OnRotationProgress;
            return Task.CompletedTask;
        }

        #endregion

        #region Private-Methods

        private void OnRotationProgress(object? sender, MasterKeyRotationStatus status)
        {
            _Context?.Logging.Info("[Secrets] master key rotation to version " + status.TargetKeyVersion + ": " + status.Completed + " of " + status.Total + " re-encrypted, " + status.Failed + " failed" + (status.Running ? String.Empty : " (finished)"));
        }

        #endregion
    }
}
