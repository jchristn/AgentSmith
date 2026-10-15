namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Secrets vault in the client's tenant (/v1.0/api/tenants/{tenantId}/secrets). Reads return metadata and field
    /// names only; RevealAsync returns plaintext after a recent re-authentication (see HugoClient.ReauthenticateAsync).
    /// </summary>
    public class SecretsClient : CrudClientBase<Secret, SecretRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public SecretsClient(HugoTransport transport) : base(transport, "secrets", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace some field values. Omitted fields keep their stored values; fields named in remove are deleted.
        /// </summary>
        /// <param name="id">Secret identifier.</param>
        /// <param name="values">New values by field name, or null.</param>
        /// <param name="remove">Field names to remove, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated metadata.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Secret> SetValuesAsync(string id, Dictionary<string, string>? values, List<string>? remove = null, CancellationToken token = default)
        {
            SecretValuesRequest request = new SecretValuesRequest();
            request.Values = values;
            request.Remove = remove;
            return PutAsync<Secret>(Item(id) + "/values", request, token);
        }

        /// <summary>
        /// Resources and model endpoints that use a secret.
        /// </summary>
        /// <param name="id">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Referrers.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<SecretReferrer>> GetReferrersAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<SecretReferrer>>(Item(id) + "/referrers", null, token);
        }

        /// <summary>
        /// Describe an SSH key, service account JSON, kubeconfig, or PEM certificate without storing it.
        /// </summary>
        /// <param name="request">Content to parse.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Description.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<SecretParseResult> ParseAsync(SecretParseRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<SecretParseResult>(Collection() + "/parse", request, token);
        }

        /// <summary>
        /// Reveal plaintext values. Requires the Reveal permission and, for user sessions, a re-authentication
        /// within Secrets.RevealReauthMinutes (403 ReauthenticationRequired otherwise). Audited and rate limited.
        /// </summary>
        /// <param name="id">Secret identifier.</param>
        /// <param name="reason">Why the values are needed, 1 to 1024 characters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Plaintext values.</returns>
        /// <exception cref="ArgumentNullException">Thrown when reason is null or whitespace.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<SecretRevealResponse> RevealAsync(string id, string reason, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentNullException(nameof(reason));
            SecretRevealRequest request = new SecretRevealRequest();
            request.Reason = reason;
            return PostAsync<SecretRevealResponse>(Item(id) + "/reveal", request, token);
        }

        #endregion
    }
}
