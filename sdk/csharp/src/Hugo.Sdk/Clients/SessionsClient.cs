namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;

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
        /// List one page of authentication sessions (each carries principalName) with filters. Unset filter properties are not sent.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="query">Optional paging, ordering, and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 ValidationFailed listing the allowed values when principalType is unknown).</exception>
        public Task<EnumerationResult<AuthSession>> ListAsync(SessionFilter filter, EnumerationQuery? query = null, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            Dictionary<string, string?> filters = new Dictionary<string, string?>
            {
                { "principalType", filter.PrincipalType },
                { "userId", filter.UserId },
                { "credentialId", filter.CredentialId }
            };
            return ListAsync(query, filters, token);
        }

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
