namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// One piece of durable knowledge about a resource, such as the business meaning of a database or a table. Knowledge
    /// persists across conversations, is editable by operators, and is fed back to agents as context. It must never
    /// contain secrets or raw data rows.
    /// </summary>
    public class ResourceKnowledgeEntry
    {
        /// <summary>
        /// Scope, defined by the connector (for example <c>database</c>, <c>table</c>, <c>bucket</c>, <c>host</c>).
        /// </summary>
        public string Scope { get; set; } = String.Empty;

        /// <summary>
        /// Key within the scope (for example <c>public.orders</c> for a table, empty for the database itself).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Markdown content written for an LLM.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Who produced the content.
        /// </summary>
        public KnowledgeSourceEnum Source { get; set; } = KnowledgeSourceEnum.Operator;

        /// <summary>
        /// Non-secret description of who last changed it (user email, agent profile, or model name).
        /// </summary>
        public string UpdatedBy { get; set; } = String.Empty;

        /// <summary>
        /// Version, incremented by the store on every change.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Last update time, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
