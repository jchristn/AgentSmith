namespace Hugo.Server.Services
{
    using WatsonWebserver.Core;

    /// <summary>
    /// Observes every completed HTTP request from the PostRouting hook (request history capture, metrics).
    /// Implementations must not block and must not throw.
    /// </summary>
    public interface IRequestObserver
    {
        /// <summary>
        /// Observe a completed request.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        void Observe(HttpContextBase context);
    }
}
