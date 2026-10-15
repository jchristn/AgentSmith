namespace Hugo.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;

    /// <summary>
    /// Secret data access. A secret and its encrypted values (secret_values) are always written together in one
    /// transaction. Reads load the values unless stated otherwise.
    /// </summary>
    public interface ISecretMethods
    {
        /// <summary>
        /// Create a secret and its values.
        /// </summary>
        /// <param name="obj">Secret, with <see cref="Secret.Values"/> already encrypted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created secret.</returns>
        Task<Secret> CreateAsync(Secret obj, CancellationToken token = default);

        /// <summary>
        /// Read a secret by identifier in any tenant (internal use: rotation, background jobs).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Secret or null.</returns>
        Task<Secret?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a secret by identifier within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Secret or null (also null when it belongs to another tenant).</returns>
        Task<Secret?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a secret by name within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Secret or null.</returns>
        Task<Secret?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update a secret's metadata and replace its value rows with <see cref="Secret.Values"/>, in one transaction.
        /// </summary>
        /// <param name="obj">Secret.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated secret.</returns>
        Task<Secret> UpdateAsync(Secret obj, CancellationToken token = default);

        /// <summary>
        /// Delete a secret and its values.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Whether a secret exists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when present.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate secrets (metadata and field names; ciphertexts are not loaded).
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null for every tenant.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="filter">Optional filters (kind), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of secrets.</returns>
        Task<EnumerationResult<Secret>> EnumerateAsync(string? tenantId, EnumerationQuery query, SecretFilter? filter = null, CancellationToken token = default);

        /// <summary>
        /// Read up to <paramref name="maxResults"/> value rows whose key version differs from <paramref name="keyVersion"/>,
        /// across every tenant (master key rotation).
        /// </summary>
        /// <param name="keyVersion">Current key version.</param>
        /// <param name="maxResults">Batch size. Minimum 1, maximum 10000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value rows.</returns>
        Task<List<SecretValue>> ReadValuesNotAtVersionAsync(int keyVersion, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Count value rows whose key version differs from <paramref name="keyVersion"/>.
        /// </summary>
        /// <param name="keyVersion">Current key version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountValuesNotAtVersionAsync(int keyVersion, CancellationToken token = default);

        /// <summary>
        /// Count every value row.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountValuesAsync(CancellationToken token = default);

        /// <summary>
        /// Replace the ciphertexts of value rows (master key rotation) and set each owning secret's key version to the
        /// lowest version among its values, in one transaction.
        /// </summary>
        /// <param name="values">Rows with their new <see cref="SecretValue.Ciphertext"/> and <see cref="SecretValue.KeyVersion"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows updated.</returns>
        Task<int> UpdateCiphertextsAsync(List<SecretValue> values, CancellationToken token = default);
    }
}
