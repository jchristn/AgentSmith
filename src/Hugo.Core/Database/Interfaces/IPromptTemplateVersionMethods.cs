namespace Hugo.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// PromptTemplateVersion data access methods.
    /// </summary>
    public interface IPromptTemplateVersionMethods
    {
        /// <summary>
        /// Create a PromptTemplateVersion, including child rows.
        /// </summary>
        /// <param name="obj">Record to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created record.</returns>
        Task<PromptTemplateVersion> CreateAsync(PromptTemplateVersion obj, CancellationToken token = default);

        /// <summary>
        /// Read a PromptTemplateVersion by identifier without tenant scoping. For internal and system-administrator use.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<PromptTemplateVersion?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a PromptTemplateVersion by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        Task<PromptTemplateVersion?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a PromptTemplateVersion, replacing child rows.
        /// </summary>
        /// <param name="obj">Record to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated record.</returns>
        Task<PromptTemplateVersion> UpdateAsync(PromptTemplateVersion obj, CancellationToken token = default);

        /// <summary>
        /// Delete a PromptTemplateVersion and its child rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check whether a PromptTemplateVersion exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate PromptTemplateVersion records.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to enumerate across tenants (system administrators only).</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of records.</returns>
        Task<EnumerationResult<PromptTemplateVersion>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// List versions of a template, newest first.
        /// </summary>
        /// <param name="templateId">Template identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Versions.</returns>
        Task<List<PromptTemplateVersion>> ListByTemplateAsync(string templateId, CancellationToken token = default);

        /// <summary>
        /// Read one version of a template.
        /// </summary>
        /// <param name="templateId">Template identifier.</param>
        /// <param name="version">Version number.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version or null.</returns>
        Task<PromptTemplateVersion?> ReadByVersionAsync(string templateId, int version, CancellationToken token = default);

        /// <summary>
        /// Delete every version of a template.
        /// </summary>
        /// <param name="templateId">Template identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteByTemplateAsync(string templateId, CancellationToken token = default);
    }
}
