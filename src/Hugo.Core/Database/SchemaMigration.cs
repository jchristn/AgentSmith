namespace Hugo.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A versioned, tracked schema migration.
    /// </summary>
    public class SchemaMigration
    {
        #region Public-Members

        /// <summary>
        /// Version number, ascending.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Ordered statements, each safe to run once inside a transaction.
        /// </summary>
        public List<string> Statements { get; }

        /// <summary>
        /// Columns added to existing tables, applied before <see cref="Statements"/>. Each is added only when missing.
        /// </summary>
        public List<SchemaColumnAddition> Columns { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="description">Description.</param>
        /// <param name="statements">Statements.</param>
        /// <exception cref="ArgumentNullException">Thrown when description or statements is null.</exception>
        public SchemaMigration(int version, string description, List<string> statements)
            : this(version, description, statements, new List<SchemaColumnAddition>())
        {
        }

        /// <summary>
        /// Instantiate with column additions.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="description">Description.</param>
        /// <param name="statements">Statements.</param>
        /// <param name="columns">Columns added to existing tables.</param>
        /// <exception cref="ArgumentNullException">Thrown when description, statements, or columns is null.</exception>
        public SchemaMigration(int version, string description, List<string> statements, List<SchemaColumnAddition> columns)
        {
            Version = version;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Statements = statements ?? throw new ArgumentNullException(nameof(statements));
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        }

        #endregion
    }
}
