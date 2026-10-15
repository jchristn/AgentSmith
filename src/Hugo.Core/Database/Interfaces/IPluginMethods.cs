namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Connector plugin records (global). Implementations must be thread-safe.
    /// </summary>
    public interface IPluginMethods
    {
        /// <summary>
        /// Insert or update a plugin record by name, replacing its connector types.
        /// </summary>
        /// <param name="obj">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<PluginRecord> UpsertAsync(PluginRecord obj, CancellationToken token = default);

        /// <summary>
        /// Read a plugin record by name.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record or null.</returns>
        Task<PluginRecord?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Every plugin record, ordered by name.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records.</returns>
        Task<List<PluginRecord>> ListAsync(CancellationToken token = default);
    }
}
