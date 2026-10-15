namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// A full crawl handed to a tool, with whether it came from the cache. Immutable and thread-safe (the crawl itself
    /// must be treated as read-only because it is shared through the cache).
    /// </summary>
    public class CrawlSnapshot
    {
        /// <summary>
        /// Crawl.
        /// </summary>
        public SchemaDetail Detail { get; }

        /// <summary>
        /// Whether the crawl was served from the cache.
        /// </summary>
        public bool Cached { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="detail">Crawl.</param>
        /// <param name="cached">Whether it came from the cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when detail is null.</exception>
        public CrawlSnapshot(SchemaDetail detail, bool cached)
        {
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            Cached = cached;
        }
    }
}
