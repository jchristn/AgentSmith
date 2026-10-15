namespace Hugo.Sdk.Clients
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;

    /// <summary>
    /// Roles in the client's tenant, including built-in roles (/v1.0/api/tenants/{tenantId}/roles).
    /// </summary>
    public class RolesClient : CrudClientBase<Role, RoleRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public RolesClient(HugoTransport transport) : base(transport, "roles", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Clone a role (built-in or custom) into a new custom role with the same permissions.
        /// </summary>
        /// <param name="id">Role identifier.</param>
        /// <param name="request">Name, description, and active flag of the copy.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>New role.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Role> CloneAsync(string id, RoleRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<Role>(Item(id) + "/clone", request, token);
        }

        #endregion
    }
}
