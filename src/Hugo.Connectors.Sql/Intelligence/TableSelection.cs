namespace Hugo.Connectors.Sql.Intelligence
{
    using System.Collections.Generic;

    /// <summary>
    /// The JSON a model returns when asked which tables a question needs. Not thread-safe for mutation.
    /// </summary>
    public class TableSelection
    {
        /// <summary>
        /// Table names (bare or <c>schema.table</c>), or null.
        /// </summary>
        public List<string>? Tables { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TableSelection()
        {
        }
    }
}
