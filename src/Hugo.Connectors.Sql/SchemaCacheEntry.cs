namespace Hugo.Connectors.Sql
{
    using System;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// One cached schema crawl. Immutable and thread-safe.
    /// </summary>
    public class SchemaCacheEntry
    {
        /// <summary>
        /// Crawl result.
        /// </summary>
        public SchemaDetail Detail { get; }

        /// <summary>
        /// UTC expiry.
        /// </summary>
        public DateTime ExpiresUtc { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="detail">Crawl result.</param>
        /// <param name="expiresUtc">UTC expiry.</param>
        /// <exception cref="ArgumentNullException">Thrown when detail is null.</exception>
        public SchemaCacheEntry(SchemaDetail detail, DateTime expiresUtc)
        {
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            ExpiresUtc = expiresUtc;
        }
    }
}
