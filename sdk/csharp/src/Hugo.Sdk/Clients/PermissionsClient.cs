namespace Hugo.Sdk.Clients
{
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;

    /// <summary>
    /// Permissions in the client's tenant, including built-ins (/v1.0/api/tenants/{tenantId}/permissions).
    /// </summary>
    public class PermissionsClient : CrudClientBase<Permission, PermissionRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public PermissionsClient(HugoTransport transport) : base(transport, "permissions", true)
        {
        }

        #endregion
    }
}
