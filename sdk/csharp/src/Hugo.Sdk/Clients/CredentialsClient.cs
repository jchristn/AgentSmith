namespace Hugo.Sdk.Clients
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// API credentials in the client's tenant (/v1.0/api/tenants/{tenantId}/credentials). The secret key is
    /// returned only by CreateAsync and RotateAsync.
    /// </summary>
    public class CredentialsClient : ReadClientBase<Credential>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public CredentialsClient(HugoTransport transport) : base(transport, "credentials", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a credential. The response carries the secret key once.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential with its access key and secret key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<CredentialCreateResponse> CreateAsync(CredentialRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<CredentialCreateResponse>(Collection(), request, token);
        }

        /// <summary>
        /// Update a credential's name, auth mode, expiry, or active flag.
        /// </summary>
        /// <param name="id">Credential identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated credential.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Credential> UpdateAsync(string id, CredentialRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PutAsync<Credential>(Item(id), request, token);
        }

        /// <summary>
        /// Rotate a credential's secret key. The response carries the new secret key once.
        /// </summary>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential with its new secret key.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<CredentialCreateResponse> RotateAsync(string id, CancellationToken token = default)
        {
            return PostAsync<CredentialCreateResponse>(Item(id) + "/rotate", null, token);
        }

        /// <summary>
        /// Effective permissions of a credential (the credential itself, or with Credential Admin).
        /// </summary>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Effective permissions.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EffectivePermissions> GetPermissionsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<EffectivePermissions>(Item(id) + "/permissions", null, token);
        }

        #endregion
    }
}
