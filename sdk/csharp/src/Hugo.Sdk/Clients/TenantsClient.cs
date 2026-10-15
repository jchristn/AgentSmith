namespace Hugo.Sdk.Clients
{
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;

    /// <summary>
    /// Tenants (/v1.0/api/tenants). Listing returns the tenants visible to the caller; create, update, and delete
    /// need system administrator rights.
    /// </summary>
    public class TenantsClient : CrudClientBase<Tenant, TenantRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public TenantsClient(HugoTransport transport) : base(transport, "tenants", false)
        {
        }

        #endregion
    }
}
