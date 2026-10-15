namespace Hugo.Connectors.Sql
{
    using System;

    /// <summary>
    /// Name and engine type of one result column. Not thread-safe for mutation.
    /// </summary>
    public class SqlColumnInfo
    {
        /// <summary>
        /// Column name as returned by the engine (empty names become <c>column1</c>, <c>column2</c>, ...).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Engine data type name (for example <c>int4</c>, <c>varchar</c>, <c>INTEGER</c>).
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SqlColumnInfo()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="name">Column name.</param>
        /// <param name="type">Engine data type.</param>
        public SqlColumnInfo(string name, string type)
        {
            Name = name ?? String.Empty;
            Type = type ?? String.Empty;
        }
    }
}
