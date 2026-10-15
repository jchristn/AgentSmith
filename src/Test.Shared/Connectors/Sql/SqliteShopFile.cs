namespace Test.Shared.Connectors.Sql
{
    using Microsoft.Data.Sqlite;
    using System;
    using System.IO;

    /// <summary>
    /// A temporary SQLite database seeded from <c>docker/testbed/sql/seed/sqlite.sql</c>. Disposing deletes the file.
    /// Not thread-safe.
    /// </summary>
    public class SqliteShopFile : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Absolute path of the database file.
        /// </summary>
        public string Path { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create and seed a temporary database.
        /// </summary>
        public SqliteShopFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hugo-sql-test-" + Guid.NewGuid().ToString("N") + ".db");
            string seed = File.ReadAllText(SqlTestSupport.RepositoryFile(System.IO.Path.Combine("docker", "testbed", "sql", "seed", "sqlite.sql")));
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
            using (SqliteConnection connection = new SqliteConnection(builder.ToString()))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = seed;
                    command.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Delete the file.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Delete the file.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                SqliteConnection.ClearAllPools();
                foreach (string suffix in new[] { String.Empty, "-wal", "-shm", "-journal" })
                {
                    try
                    {
                        if (File.Exists(Path + suffix)) File.Delete(Path + suffix);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            _Disposed = true;
        }

        #endregion
    }
}
