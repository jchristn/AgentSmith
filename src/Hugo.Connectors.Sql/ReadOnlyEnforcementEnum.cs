namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// How a SQL resource enforces read-only access beyond Hugo's statement classifier.
    /// </summary>
    public enum ReadOnlyEnforcementEnum
    {
        /// <summary>
        /// Run reads inside a read-only session or transaction where the engine supports it (PostgreSQL
        /// <c>SET TRANSACTION READ ONLY</c>, MySQL <c>START TRANSACTION READ ONLY</c>, SQLite <c>Mode=ReadOnly</c>).
        /// SQL Server has no such mode and falls back to <see cref="ClassifierOnly"/>. Default.
        /// </summary>
        Session = 0,

        /// <summary>
        /// Rely on the statement classifier alone. Use when the engine or a proxy rejects read-only transactions.
        /// </summary>
        ClassifierOnly = 1
    }
}
