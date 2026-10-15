namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// A2A task and task artifact data access. Thread-safe.
    /// </summary>
    public interface IA2ATaskMethods
    {
        /// <summary>
        /// Create a task.
        /// </summary>
        /// <param name="obj">Task.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<A2ATask> CreateAsync(A2ATask obj, CancellationToken token = default);

        /// <summary>
        /// Read a task.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Task identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task<A2ATask?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a task's state, status message, pending approval, and usage.
        /// </summary>
        /// <param name="obj">Task.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<A2ATask> UpdateAsync(A2ATask obj, CancellationToken token = default);

        /// <summary>
        /// Enumerate a profile's tasks, newest first.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="agentProfileId">Agent profile identifier.</param>
        /// <param name="principalId">Only this principal's tasks, or null for all.</param>
        /// <param name="contextId">Only this context, or null.</param>
        /// <param name="state">Only this state, or null.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        Task<EnumerationResult<A2ATask>> EnumerateAsync(string tenantId, string agentProfileId, string? principalId, string? contextId, A2ATaskStateEnum? state, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// List tasks in any of the given states, across tenants (restart recovery).
        /// </summary>
        /// <param name="states">States.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tasks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when states is null.</exception>
        Task<List<A2ATask>> ListByStateAsync(List<A2ATaskStateEnum> states, CancellationToken token = default);

        /// <summary>
        /// Store an artifact of a task.
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact.</returns>
        /// <exception cref="ArgumentNullException">Thrown when artifact is null.</exception>
        Task<A2ATaskArtifact> AddArtifactAsync(A2ATaskArtifact artifact, CancellationToken token = default);

        /// <summary>
        /// List a task's artifacts in creation order.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="taskId">Task identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Artifacts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task<List<A2ATaskArtifact>> ListArtifactsAsync(string tenantId, string taskId, CancellationToken token = default);
    }
}
