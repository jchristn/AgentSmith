namespace Hugo.Core.Database.Sqlite.Queries
{
    using System;
    using Hugo.Core.Database.Sql;
    using Hugo.Core.Enums;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite dialect.
    /// </summary>
    public class SqliteDialect : SqlDialect
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum DatabaseType
        {
            get
            {
                return DatabaseTypeEnum.Sqlite;
            }
        }

        /// <inheritdoc />
        public override bool SupportsReturning
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc />
        public override string TypeText
        {
            get
            {
                return "TEXT";
            }
        }

        /// <inheritdoc />
        public override string TypeBool
        {
            get
            {
                return "INTEGER";
            }
        }

        /// <inheritdoc />
        public override string TypeDouble
        {
            get
            {
                return "REAL";
            }
        }

        /// <inheritdoc />
        public override string TypeTimestamp
        {
            get
            {
                return "TEXT";
            }
        }

        /// <inheritdoc />
        public override string TypeAutoIncrementPrimaryKey
        {
            get
            {
                return "INTEGER PRIMARY KEY AUTOINCREMENT";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string Quote(string identifier)
        {
            if (String.IsNullOrEmpty(identifier)) throw new ArgumentNullException(nameof(identifier));
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }

        /// <inheritdoc />
        public override string QualifiedReference(string? schema, string rawName)
        {
            return Quote(rawName);
        }

        /// <inheritdoc />
        public override string BooleanLiteral(bool value)
        {
            return value ? "1" : "0";
        }

        /// <inheritdoc />
        public override string CaseInsensitiveLike(string columnReference, string parameterName)
        {
            return "LOWER(" + columnReference + ") LIKE LOWER(@" + parameterName + ")";
        }

        /// <inheritdoc />
        public override string TableExistsProbe(string? schema)
        {
            return "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1;";
        }

        /// <inheritdoc />
        public override string ColumnExistsProbe(string? schema)
        {
            return "SELECT 1 FROM pragma_table_info(@table) WHERE name = @column LIMIT 1;";
        }

        /// <inheritdoc />
        public override bool IsTransientConflict(Exception exception)
        {
            if (exception is SqliteException sqlite) return sqlite.SqliteErrorCode == 5 || sqlite.SqliteErrorCode == 6;
            return false;
        }

        #endregion
    }
}
