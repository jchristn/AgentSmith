namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Model endpoint health history (model_endpoint_health): a small, bounded table of recent health checks per endpoint,
    /// written by the health sweep and by validate.
    /// </summary>
    public interface IModelEndpointHealthMethods
    {
        /// <summary>
        /// Record a health check and drop the endpoint's oldest records beyond the limit, in one transaction.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="keep">Records to keep per endpoint, newest first. Minimum 1, maximum 10000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        Task<ModelEndpointHealthRecord> AddAsync(ModelEndpointHealthRecord record, int keep, CancellationToken token = default);

        /// <summary>
        /// Recent health checks of one endpoint, newest first.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="modelEndpointId">Model endpoint identifier.</param>
        /// <param name="maxResults">Maximum records to return. Minimum 1, maximum 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records, newest first.</returns>
        Task<List<ModelEndpointHealthRecord>> ListAsync(string tenantId, string modelEndpointId, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Delete every health record of one endpoint (when the endpoint is deleted).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="modelEndpointId">Model endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteByEndpointAsync(string tenantId, string modelEndpointId, CancellationToken token = default);
    }
}
