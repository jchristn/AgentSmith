namespace Hugo.Server.Modules
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Server.Routes;

    /// <summary>
    /// A server feature area. Modules initialize in order (publishing services to the context), then register routes,
    /// then start background work. They stop in reverse order.
    /// </summary>
    public interface IServerModule
    {
        /// <summary>
        /// Module name for logs.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Create services and publish them to the context.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task InitializeAsync(ServerContext context, CancellationToken token);

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="routes">Route registrar.</param>
        void RegisterRoutes(ServerContext context, RouteRegistrar routes);

        /// <summary>
        /// Start background work.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task StartAsync(ServerContext context, CancellationToken token);

        /// <summary>
        /// Stop background work.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task StopAsync(CancellationToken token);
    }
}
