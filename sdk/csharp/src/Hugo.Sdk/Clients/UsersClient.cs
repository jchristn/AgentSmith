namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Users in the client's tenant (/v1.0/api/tenants/{tenantId}/users) and the signed-in user (/v1.0/api/users/me).
    /// </summary>
    public class UsersClient : CrudClientBase<User, UserRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public UsersClient(HugoTransport transport) : base(transport, "users", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the signed-in user.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status (400 NotAUser for credentials and API keys).</exception>
        public Task<User> GetMeAsync(CancellationToken token = default)
        {
            return GetAsync<User>("/v1.0/api/users/me", null, token);
        }

        /// <summary>
        /// Update the signed-in user's first name, last name, preferred locale, and default agent profile. Other
        /// request properties are ignored by the server.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated user.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<User> UpdateMeAsync(UserRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PutAsync<User>("/v1.0/api/users/me", request, token);
        }

        /// <summary>
        /// Effective permissions of a user (self, or with User Admin).
        /// </summary>
        /// <param name="id">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Effective permissions.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EffectivePermissions> GetPermissionsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<EffectivePermissions>(Item(id) + "/permissions", null, token);
        }

        /// <summary>
        /// Set a user's password as an administrator. By default the user must change it at next sign-in.
        /// </summary>
        /// <param name="id">User identifier.</param>
        /// <param name="newPassword">New password, 8 to 256 characters.</param>
        /// <param name="mustChangePassword">Whether the user must change it at next sign-in; null uses the server default (true).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated user.</returns>
        /// <exception cref="ArgumentNullException">Thrown when newPassword is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<User> ResetPasswordAsync(string id, string newPassword, bool? mustChangePassword = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(newPassword)) throw new ArgumentNullException(nameof(newPassword));
            PasswordChangeRequest request = new PasswordChangeRequest();
            request.NewPassword = newPassword;
            Dictionary<string, string?> query = new Dictionary<string, string?>();
            if (mustChangePassword.HasValue) query["mustChangePassword"] = mustChangePassword.Value ? "true" : "false";
            return Transport.SendRequiredAsync<User>(HttpMethod.Post, Item(id) + "/password", request, query, token);
        }

        #endregion
    }
}
