namespace Hugo.Connectors.Redis
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// Opens Redis connections and reuses them through the resource's session pool. Thread-safe.
    /// </summary>
    public static class RedisSessionFactory
    {
        #region Public-Methods

        /// <summary>
        /// Get the pooled session of a resource, connecting when none exists.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when settings are invalid or the connection fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public static Task<RedisSession> GetAsync(ConnectorResourceContext resource, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.Sessions.GetOrCreateAsync(resource.SessionKey("redis"), ct => ConnectAsync(resource, ct), token);
        }

        /// <summary>
        /// Drop the pooled session of a resource (after a connection failure).
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static void Invalidate(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            resource.Sessions.Invalidate(resource.SessionKey("redis"));
        }

        /// <summary>
        /// Open a new, unpooled connection. The caller owns and disposes the session.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="token">Cancellation token. A connection that completes after cancellation is closed.</param>
        /// <returns>Session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when settings are invalid or the connection fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public static async Task<RedisSession> ConnectAsync(ConnectorResourceContext resource, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            RedisConnectionOptions options = RedisConnectionOptions.FromSettings(resource.Settings);
            string? password = resource.Secrets.Get(RedisSettingKeys.PasswordSlot, RedisSettingKeys.PasswordField);
            string? secretUser = resource.Secrets.Get(RedisSettingKeys.PasswordSlot, RedisSettingKeys.UsernameField);
            ConfigurationOptions config = options.BuildConfiguration(password, secretUser);

            Task<ConnectionMultiplexer> connect;
            try
            {
                connect = ConnectionMultiplexer.ConnectAsync(config);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw RedisErrorMapper.Map(e, "connect to " + options.EndpointsText());
            }

            ConnectionMultiplexer mux;
            try
            {
                mux = await connect.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _ = connect.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
                    else if (t.Exception != null) t.Exception.Handle(x => true);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }
            catch (Exception e)
            {
                throw RedisErrorMapper.Map(e, "connect to " + options.EndpointsText());
            }

            resource.Logger.Debug("redis connected to " + options.EndpointsText() + " (" + mux.GetServers().Length + " server(s))");
            return new RedisSession(mux, options);
        }

        #endregion
    }
}
