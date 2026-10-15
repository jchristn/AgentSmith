namespace Hugo.Core.Database
{
    using System;

    /// <summary>
    /// A column a migration adds to an existing table. The driver adds it only when the column is missing, so a
    /// migration that adds columns is safe on a database whose tables were created from a newer reference schema.
    /// </summary>
    public class SchemaColumnAddition
    {
        #region Public-Members

        /// <summary>
        /// Unprefixed table name.
        /// </summary>
        public string Table { get; }

        /// <summary>
        /// Column name.
        /// </summary>
        public string Column { get; }

        /// <summary>
        /// Column type and constraints, for example "BIGINT NOT NULL DEFAULT 0".
        /// </summary>
        public string Definition { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="table">Unprefixed table name.</param>
        /// <param name="column">Column name.</param>
        /// <param name="definition">Column type and constraints.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public SchemaColumnAddition(string table, string column, string definition)
        {
            if (String.IsNullOrEmpty(table)) throw new ArgumentNullException(nameof(table));
            if (String.IsNullOrEmpty(column)) throw new ArgumentNullException(nameof(column));
            if (String.IsNullOrEmpty(definition)) throw new ArgumentNullException(nameof(definition));
            Table = table;
            Column = column;
            Definition = definition;
        }

        #endregion
    }
}
