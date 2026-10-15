namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;

    /// <summary>
    /// ClusterSetting data access methods.
    /// </summary>
    public interface IClusterSettingMethods
    {
        /// <summary>
        /// Create a ClusterSetting, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<ClusterSetting> CreateAsync(ClusterSetting obj, CancellationToken token = default);

        /// <summary>
        /// Read a ClusterSetting by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<ClusterSetting?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a ClusterSetting, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<ClusterSetting> UpdateAsync(ClusterSetting obj, CancellationToken token = default);

        /// <summary>
        /// Delete a ClusterSetting and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a ClusterSetting exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate ClusterSetting records.
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<ClusterSetting>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Read a setting by key.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Setting or null.</returns>
        Task<ClusterSetting?> ReadByKeyAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Insert or update a setting by key.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="value">Value.</param>
        /// <param name="updatedBy">Principal making the change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored setting.</returns>
        Task<ClusterSetting> UpsertAsync(string key, string? value, string? updatedBy, CancellationToken token = default);

        /// <summary>
        /// All settings.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        Task<List<ClusterSetting>> ListAllAsync(CancellationToken token = default);
    }
}
