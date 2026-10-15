namespace Hugo.Connectors.Storage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Creates time-limited signed URLs for one bucket or container. Blobject's <c>GenerateUrl</c> returns an unsigned
    /// object URL, so presigning uses the provider SDK that Blobject already depends on. Implementations are thread-safe
    /// and sign locally without a network call.
    /// </summary>
    public interface IStoragePresigner : IDisposable
    {
        /// <summary>
        /// Create a signed URL.
        /// </summary>
        /// <param name="key">Object key, already validated.</param>
        /// <param name="method">Method the URL authorizes.</param>
        /// <param name="expiry">Lifetime of the URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The URL.</returns>
        /// <exception cref="Hugo.Connectors.Abstractions.ConnectorException">Code <c>not_supported</c> when the
        /// credentials cannot sign URLs.</exception>
        Task<string> PresignAsync(string key, StoragePresignMethodEnum method, TimeSpan expiry, CancellationToken token = default);
    }
}
