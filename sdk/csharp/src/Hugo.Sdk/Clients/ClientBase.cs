namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;

    /// <summary>
    /// Base class of every SDK module: path helpers and request shortcuts over the shared transport.
    /// </summary>
    public abstract class ClientBase
    {
        #region Public-Members

        /// <summary>
        /// Transport shared with the client.
        /// </summary>
        public HugoTransport Transport { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        /// <exception cref="ArgumentNullException">Thrown when transport is null.</exception>
        protected ClientBase(HugoTransport transport)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build a tenant-scoped management path, /v1.0/api/tenants/{tenantId}{suffix}, using the client's tenant.
        /// </summary>
        /// <param name="suffix">Suffix beginning with a slash, or empty.</param>
        /// <returns>Path.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no tenant is set.</exception>
        protected string TenantPath(string suffix)
        {
            if (String.IsNullOrEmpty(Transport.TenantId)) throw new InvalidOperationException("Set TenantId on the client (or log in) before calling tenant-scoped APIs.");
            return "/v1.0/api/tenants/" + Uri.EscapeDataString(Transport.TenantId) + suffix;
        }

        /// <summary>
        /// Escape one path segment.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Escaped segment.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null or empty.</exception>
        protected static string Segment(string value)
        {
            if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(value));
            return Uri.EscapeDataString(value);
        }

        /// <summary>
        /// GET and deserialize a required response.
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="path">Path.</param>
        /// <param name="query">Optional query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        protected Task<T> GetAsync<T>(string path, IDictionary<string, string?>? query, CancellationToken token)
        {
            return Transport.SendRequiredAsync<T>(HttpMethod.Get, path, null, query, token);
        }

        /// <summary>
        /// POST and deserialize a required response.
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="path">Path.</param>
        /// <param name="body">Body, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        protected Task<T> PostAsync<T>(string path, object? body, CancellationToken token)
        {
            return Transport.SendRequiredAsync<T>(HttpMethod.Post, path, body, null, token);
        }

        /// <summary>
        /// PUT and deserialize a required response.
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="path">Path.</param>
        /// <param name="body">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        protected Task<T> PutAsync<T>(string path, object? body, CancellationToken token)
        {
            return Transport.SendRequiredAsync<T>(HttpMethod.Put, path, body, null, token);
        }

        /// <summary>
        /// DELETE, ignoring any response body.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <param name="query">Optional query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected async Task DeleteAsync(string path, IDictionary<string, string?>? query, CancellationToken token)
        {
            await Transport.SendTextAsync(HttpMethod.Delete, path, null, query, null, true, token).ConfigureAwait(false);
        }

        #endregion
    }
}
