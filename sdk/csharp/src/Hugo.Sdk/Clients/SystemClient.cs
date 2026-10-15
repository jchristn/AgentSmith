namespace Hugo.Sdk.Clients
{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Health, server information, observability status, master key rotation, and restart.
    /// </summary>
    public class SystemClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public SystemClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Health (anonymous). Throws HugoException with status 503 when the server is degraded, migrating, or draining.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task<HealthResponse> GetHealthAsync(CancellationToken token = default)
        {
            string text = await Transport.SendTextAsync(HttpMethod.Get, "/v1.0/api/health", null, null, null, false, token).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<HealthResponse>(text, HugoTransport.JsonOptions) ?? new HealthResponse();
        }

        /// <summary>
        /// Liveness (anonymous): 200 while the process runs.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task<HealthResponse> GetLivenessAsync(CancellationToken token = default)
        {
            string text = await Transport.SendTextAsync(HttpMethod.Get, "/v1.0/api/health/live", null, null, null, false, token).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<HealthResponse>(text, HugoTransport.JsonOptions) ?? new HealthResponse();
        }

        /// <summary>
        /// Server version, endpoints, and feature flags.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server information.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ServerInfo> GetInfoAsync(CancellationToken token = default)
        {
            return GetAsync<ServerInfo>("/v1.0/api/info", null, token);
        }

        /// <summary>
        /// Reachability of the configured observability services.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status per service.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<ObservabilityServiceStatus>> GetObservabilityStatusAsync(CancellationToken token = default)
        {
            return GetAsync<List<ObservabilityServiceStatus>>("/v1.0/api/observability/status", null, token);
        }

        /// <summary>
        /// Master key version and rotation progress (system administrators).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<MasterKeyRotationStatus> GetMasterKeyStatusAsync(CancellationToken token = default)
        {
            return GetAsync<MasterKeyRotationStatus>("/v1.0/api/system/master-key", null, token);
        }

        /// <summary>
        /// Re-encrypt every secret value and credential secret under the current master key version (system administrators).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<MasterKeyRotationStatus> RotateMasterKeyAsync(CancellationToken token = default)
        {
            return PostAsync<MasterKeyRotationStatus>("/v1.0/api/system/master-key/rotate", null, token);
        }

        /// <summary>
        /// Restart the server process (system administrators). The container runtime restarts it.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Restart response.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<RestartResponse> RestartAsync(CancellationToken token = default)
        {
            return PostAsync<RestartResponse>("/v1.0/api/system/restart", null, token);
        }

        #endregion
    }
}
