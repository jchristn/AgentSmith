namespace Hugo.Sdk.Clients
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Authentication sessions in the client's tenant (/v1.0/api/tenants/{tenantId}/sessions). DeleteAsync and
    /// RevokeAsync both revoke a session.
    /// </summary>
    public class SessionsClient : ReadClientBase<AuthSession>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public SessionsClient(HugoTransport transport) : base(transport, "sessions", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Revoke a session. Tokens of a revoked session stop working immediately.
        /// </summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task RevokeAsync(string id, CancellationToken token = default)
        {
            return DeleteAsync(id, token);
        }

        #endregion
    }
}
