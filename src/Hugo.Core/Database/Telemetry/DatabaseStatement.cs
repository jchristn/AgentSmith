namespace Hugo.Core.Database.Telemetry
{
    using System;

    /// <summary>
    /// The low-cardinality name of a SQL statement (P13.24, P13.25): its operation (select, insert, update, delete,
    /// ddl, other) and the first table it names, with schema, prefix quoting, and parameter values never included.
    /// Immutable and thread-safe.
    /// </summary>
    public class DatabaseStatement
    {
        #region Public-Members

        /// <summary>
        /// Operation: select, insert, update, delete, upsert, ddl, transaction, or other.
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Table name without schema or quotes, or none.
        /// </summary>
        public string Table { get; }

        /// <summary>
        /// Statement name used for spans: "&lt;operation&gt; &lt;table&gt;".
        /// </summary>
        public string Name { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="operation">Operation.</param>
        /// <param name="table">Table.</param>
        public DatabaseStatement(string operation, string table)
        {
            Operation = String.IsNullOrEmpty(operation) ? "other" : operation;
            Table = String.IsNullOrEmpty(table) ? "none" : table;
            Name = Operation + " " + Table;
        }

        #endregion
    }
}
