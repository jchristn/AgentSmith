namespace Hugo.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Master key version (canary) data access. The table is global.
    /// </summary>
    public interface IMasterKeyVersionMethods
    {
        /// <summary>
        /// Read every key version, ascending.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Key versions.</returns>
        Task<List<MasterKeyVersion>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Read one key version.
        /// </summary>
        /// <param name="keyVersion">Key version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Key version or null.</returns>
        Task<MasterKeyVersion?> ReadAsync(int keyVersion, CancellationToken token = default);

        /// <summary>
        /// Create a key version, or replace the canary of an existing one.
        /// </summary>
        /// <param name="obj">Key version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored key version.</returns>
        Task<MasterKeyVersion> UpsertAsync(MasterKeyVersion obj, CancellationToken token = default);

        /// <summary>
        /// Mark one key version current and every other version not current.
        /// </summary>
        /// <param name="keyVersion">Key version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetCurrentAsync(int keyVersion, CancellationToken token = default);
    }
}
