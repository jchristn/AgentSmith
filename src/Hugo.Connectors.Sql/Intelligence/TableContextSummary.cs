namespace Hugo.Connectors.Sql.Intelligence
{
    using System;

    /// <summary>
    /// Durable context of one table, possibly shortened, with its provenance. Not thread-safe for mutation.
    /// </summary>
    public class TableContextSummary
    {
        /// <summary>
        /// Knowledge key (<c>schema.table</c>).
        /// </summary>
        public string Table { get; set; } = String.Empty;

        /// <summary>
        /// Who produced it: <c>Operator</c>, <c>Generated</c>, <c>Agent</c>, or <c>Derived</c>.
        /// </summary>
        public string Source { get; set; } = String.Empty;

        /// <summary>
        /// Who last changed it (non-secret).
        /// </summary>
        public string UpdatedBy { get; set; } = String.Empty;

        /// <summary>
        /// Version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Last update, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Content, shortened when <see cref="Shortened"/> is true.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Whether <see cref="Content"/> was cut; read the full text with sql_get_context.
        /// </summary>
        public bool Shortened { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TableContextSummary()
        {
        }
    }
}
