namespace Hugo.Core.Database.Sql
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Provider-specific SQL rendering: identifier quoting, pagination, boolean literals, case-insensitive matching,
    /// DDL type tokens, and transient-conflict detection. Shared data-access code is written once against
    /// <see cref="System.Data.Common"/> and parameterized by a dialect.
    /// </summary>
    public abstract class SqlDialect
    {
        #region Public-Members

        /// <summary>
        /// Provider this dialect targets.
        /// </summary>
        public abstract DatabaseTypeEnum DatabaseType { get; }

        /// <summary>
        /// Conservative maximum bind parameters in one statement. Defaults to 999 for SQLite compatibility;
        /// providers with a larger protocol limit override it. Batch writers must chunk below this limit.
        /// </summary>
        public virtual int MaximumParametersPerStatement
        {
            get
            {
                return 999;
            }
        }

        /// <summary>
        /// Whether UPDATE and DELETE statements support a RETURNING clause.
        /// </summary>
        public virtual bool SupportsReturning
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Column type for a 64-character identifier.
        /// </summary>
        public virtual string TypeId
        {
            get
            {
                return "VARCHAR(64)";
            }
        }

        /// <summary>
        /// Column type for unbounded text.
        /// </summary>
        public abstract string TypeText { get; }

        /// <summary>
        /// Column type for a 32-bit integer.
        /// </summary>
        public virtual string TypeInt
        {
            get
            {
                return "INTEGER";
            }
        }

        /// <summary>
        /// Column type for a 64-bit integer.
        /// </summary>
        public virtual string TypeBigInt
        {
            get
            {
                return "BIGINT";
            }
        }

        /// <summary>
        /// Column type for a boolean.
        /// </summary>
        public abstract string TypeBool { get; }

        /// <summary>
        /// Column type for a double.
        /// </summary>
        public abstract string TypeDouble { get; }

        /// <summary>
        /// Column type for a UTC timestamp.
        /// </summary>
        public abstract string TypeTimestamp { get; }

        /// <summary>
        /// Column definition for a 64-bit primary key the database assigns in ascending order on insert.
        /// </summary>
        public abstract string TypeAutoIncrementPrimaryKey { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Quote an identifier.
        /// </summary>
        /// <param name="identifier">Identifier.</param>
        /// <returns>Quoted identifier.</returns>
        public abstract string Quote(string identifier);

        /// <summary>
        /// Build a quoted, optionally schema-qualified table reference.
        /// </summary>
        /// <param name="schema">Optional schema.</param>
        /// <param name="rawName">Unquoted table name.</param>
        /// <returns>Reference.</returns>
        /// <exception cref="ArgumentNullException">Thrown when rawName is null or empty.</exception>
        public virtual string QualifiedReference(string? schema, string rawName)
        {
            if (String.IsNullOrEmpty(rawName)) throw new ArgumentNullException(nameof(rawName));
            if (String.IsNullOrEmpty(schema)) return Quote(rawName);
            return Quote(schema) + "." + Quote(rawName);
        }

        /// <summary>
        /// Pagination clause appended after ORDER BY, using parameters named max and skip.
        /// </summary>
        /// <returns>Clause with a leading space.</returns>
        public virtual string LimitOffsetClause()
        {
            return " LIMIT @max OFFSET @skip";
        }

        /// <summary>
        /// Render a boolean literal.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Literal.</returns>
        public abstract string BooleanLiteral(bool value);

        /// <summary>
        /// Case-insensitive substring match against a parameter holding a %value% pattern.
        /// </summary>
        /// <param name="columnReference">Column.</param>
        /// <param name="parameterName">Parameter name without prefix.</param>
        /// <returns>Clause.</returns>
        public abstract string CaseInsensitiveLike(string columnReference, string parameterName);

        /// <summary>
        /// CREATE TABLE statement that is a no-op when the table exists.
        /// </summary>
        /// <param name="schema">Optional schema.</param>
        /// <param name="rawName">Unquoted table name.</param>
        /// <param name="columnsCsv">Column definitions.</param>
        /// <returns>DDL.</returns>
        public virtual string CreateTable(string? schema, string rawName, string columnsCsv)
        {
            return "CREATE TABLE IF NOT EXISTS " + QualifiedReference(schema, rawName) + " (\n" + columnsCsv + "\n);";
        }

        /// <summary>
        /// CREATE INDEX statement that is a no-op when the index exists.
        /// </summary>
        /// <param name="indexName">Unquoted index name.</param>
        /// <param name="schema">Optional schema.</param>
        /// <param name="rawTableName">Unquoted table name.</param>
        /// <param name="columnsCsv">Quoted column list.</param>
        /// <param name="unique">Whether the index is unique.</param>
        /// <returns>DDL.</returns>
        public virtual string CreateIndex(string indexName, string? schema, string rawTableName, string columnsCsv, bool unique)
        {
            return CreateIndex(indexName, schema, rawTableName, columnsCsv, unique, null);
        }

        /// <summary>
        /// CREATE INDEX statement that is a no-op when the index exists, optionally partial (only rows matching a
        /// predicate are indexed; Sqlite and PostgreSQL both support it).
        /// </summary>
        /// <param name="indexName">Unquoted index name.</param>
        /// <param name="schema">Optional schema.</param>
        /// <param name="rawTableName">Unquoted table name.</param>
        /// <param name="columnsCsv">Quoted column list.</param>
        /// <param name="unique">Whether the index is unique.</param>
        /// <param name="where">Predicate of a partial index (without WHERE), or null for a full index.</param>
        /// <returns>DDL.</returns>
        public virtual string CreateIndex(string indexName, string? schema, string rawTableName, string columnsCsv, bool unique, string? where)
        {
            string keyword = unique ? "CREATE UNIQUE INDEX" : "CREATE INDEX";
            return keyword + " IF NOT EXISTS " + Quote(indexName) + " ON " + QualifiedReference(schema, rawTableName) + " (" + columnsCsv + ")" + (String.IsNullOrWhiteSpace(where) ? String.Empty : " WHERE " + where) + ";";
        }

        /// <summary>
        /// Build an ALTER TABLE statement that adds a column.
        /// </summary>
        /// <param name="schema">Schema, or null.</param>
        /// <param name="rawTableName">Prefixed table name.</param>
        /// <param name="column">Column name.</param>
        /// <param name="definition">Column type and constraints.</param>
        /// <returns>DDL statement.</returns>
        public virtual string AddColumn(string? schema, string rawTableName, string column, string definition)
        {
            return "ALTER TABLE " + QualifiedReference(schema, rawTableName) + " ADD COLUMN " + column + " " + definition + ";";
        }

        /// <summary>
        /// Query returning a row when a column exists, with parameters @table, @column, and (when a schema is set)
        /// @schema.
        /// </summary>
        /// <param name="schema">Schema, or null.</param>
        /// <returns>Probe SQL.</returns>
        public abstract string ColumnExistsProbe(string? schema);

        /// <summary>
        /// Query returning one row when a table exists, using parameters named name and (optionally) schema.
        /// </summary>
        /// <param name="schema">Optional schema.</param>
        /// <returns>Query.</returns>
        public abstract string TableExistsProbe(string? schema);

        /// <summary>
        /// Statement creating a schema when the provider supports schemas; null otherwise.
        /// </summary>
        /// <param name="schema">Schema.</param>
        /// <returns>DDL or null.</returns>
        public virtual string? CreateSchemaStatement(string schema)
        {
            return null;
        }

        /// <summary>
        /// Statement that takes a transaction-scoped exclusive lock on a 64-bit key held in parameter @key, released
        /// when the transaction ends; null when the provider serializes writers already (Sqlite).
        /// </summary>
        /// <returns>Statement or null.</returns>
        public virtual string? TransactionLockStatement()
        {
            return null;
        }

        /// <summary>
        /// Clause appended to a SELECT inside a transaction that locks the selected rows and leaves out rows another
        /// transaction holds, instead of waiting for them; null when the provider serializes writers already (Sqlite).
        /// </summary>
        /// <returns>Clause with a leading space, or null.</returns>
        public virtual string? SkipLockedClause()
        {
            return null;
        }

        /// <summary>
        /// Whether an exception is a transient concurrency conflict that should be retried.
        /// </summary>
        /// <param name="exception">Exception.</param>
        /// <returns>True to retry.</returns>
        public virtual bool IsTransientConflict(Exception exception)
        {
            return false;
        }

        #endregion
    }
}
