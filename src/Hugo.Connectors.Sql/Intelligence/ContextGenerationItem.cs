namespace Hugo.Connectors.Sql.Intelligence
{
    using System;

    /// <summary>
    /// Outcome of generating one context entry. Not thread-safe for mutation.
    /// </summary>
    public class ContextGenerationItem
    {
        /// <summary>
        /// Scope (<c>database</c> or <c>table</c>).
        /// </summary>
        public string Scope { get; set; } = String.Empty;

        /// <summary>
        /// Key (empty for the database, <c>schema.table</c> for a table).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// <c>generated</c>, <c>skipped</c>, or <c>failed</c>.
        /// </summary>
        public string Status { get; set; } = "skipped";

        /// <summary>
        /// Why it was skipped or failed, or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Characters stored.
        /// </summary>
        public int Chars { get; set; } = 0;

        /// <summary>
        /// Stored version, or 0.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Whether a row sample was included in the prompt (never stored).
        /// </summary>
        public bool UsedSample { get; set; } = false;

        /// <summary>
        /// Generated content, or null.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ContextGenerationItem()
        {
        }
    }
}
