namespace Hugo.Sdk.Clients
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;

    /// <summary>
    /// Credential scope assignments in the client's tenant (/v1.0/api/tenants/{tenantId}/credential-scope-assignments).
    /// </summary>
    public class CredentialScopeAssignmentsClient : CrudClientBase<CredentialScopeAssignment, AssignmentRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public CredentialScopeAssignmentsClient(HugoTransport transport) : base(transport, "credential-scope-assignments", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the assignments of one credential.
        /// </summary>
        /// <param name="credentialId">Credential identifier.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EnumerationResult<CredentialScopeAssignment>> ListForCredentialAsync(string credentialId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return ListAsync(query, new Dictionary<string, string?> { { "credentialId", credentialId } }, token);
        }

        #endregion
    }
}
