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
    /// User role assignments in the client's tenant (/v1.0/api/tenants/{tenantId}/user-role-assignments).
    /// </summary>
    public class UserRoleAssignmentsClient : CrudClientBase<UserRoleAssignment, AssignmentRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public UserRoleAssignmentsClient(HugoTransport transport) : base(transport, "user-role-assignments", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the assignments of one user.
        /// </summary>
        /// <param name="userId">User identifier.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EnumerationResult<UserRoleAssignment>> ListForUserAsync(string userId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return ListAsync(query, new Dictionary<string, string?> { { "userId", userId } }, token);
        }

        #endregion
    }
}
