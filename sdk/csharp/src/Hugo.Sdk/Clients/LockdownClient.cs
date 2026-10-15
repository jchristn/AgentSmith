namespace Hugo.Sdk.Clients
{
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Tenant lockdown (/v1.0/api/tenant/lockdown). While on, every non-Read tool call in the tenant is denied and
    /// pending approvals are cancelled.
    /// </summary>
    public class LockdownClient : ClientBase
    {
        #region Private-Members

        private static readonly string _Path = "/v1.0/api/tenant/lockdown";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public LockdownClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the tenant's lockdown state.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<LockdownState> GetAsync(CancellationToken token = default)
        {
            return GetAsync<LockdownState>(_Path, null, token);
        }

        /// <summary>
        /// Turn lockdown on.
        /// </summary>
        /// <param name="reason">Optional reason (audited).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<LockdownState> EnableAsync(string? reason = null, CancellationToken token = default)
        {
            LockdownRequest request = new LockdownRequest();
            request.Reason = reason;
            return PostAsync<LockdownState>(_Path, request, token);
        }

        /// <summary>
        /// Turn lockdown off.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<LockdownState> DisableAsync(CancellationToken token = default)
        {
            return Transport.SendRequiredAsync<LockdownState>(HttpMethod.Delete, _Path, null, null, token);
        }

        #endregion
    }
}
