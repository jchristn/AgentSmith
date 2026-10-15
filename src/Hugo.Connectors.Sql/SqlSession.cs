namespace Hugo.Connectors.Sql
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One open database connection for one tool call, optionally inside a read-only transaction. Disposing rolls back
    /// any open transaction and closes the connection (returning it to the driver's pool). Not thread-safe.
    /// </summary>
    public class SqlSession : IDisposable, IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Dialect.
        /// </summary>
        public SqlDialect Dialect { get; }

        /// <summary>
        /// Options in effect.
        /// </summary>
        public SqlResourceOptions Options { get; }

        /// <summary>
        /// Open connection.
        /// </summary>
        public DbConnection Connection { get; }

        /// <summary>
        /// Active transaction, or null.
        /// </summary>
        public DbTransaction? Transaction { get; private set; }

        /// <summary>
        /// Whether the engine itself enforces read-only access for this session (read-only transaction or read-only
        /// connection mode).
        /// </summary>
        public bool EngineReadOnly { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private SqlSession(SqlDialect dialect, SqlResourceOptions options, DbConnection connection, DbTransaction? transaction, bool engineReadOnly)
        {
            Dialect = dialect;
            Options = options;
            Connection = connection;
            Transaction = transaction;
            EngineReadOnly = engineReadOnly;
        }

        /// <summary>
        /// Open a session.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="options">Options.</param>
        /// <param name="readOnly">Whether the call must be read-only. When the resource uses
        /// <see cref="ReadOnlyEnforcementEnum.Session"/> and the engine supports it, the session is opened read-only.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Open session; the caller disposes it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when dialect or options is null.</exception>
        /// <exception cref="ConnectorException">Thrown with code <c>connection_failed</c> when the connection cannot be opened.</exception>
        public static async Task<SqlSession> OpenAsync(SqlDialect dialect, SqlResourceOptions options, bool readOnly, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(dialect);
            ArgumentNullException.ThrowIfNull(options);

            bool engineReadOnly = readOnly && options.ReadOnlyEnforcement == ReadOnlyEnforcementEnum.Session && dialect.SupportsSessionReadOnly;
            DbConnection connection = dialect.CreateConnection(options, engineReadOnly);
            try
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is DbException || e is InvalidOperationException || e is ArgumentException || e is System.Net.Sockets.SocketException || e is TimeoutException)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw new ConnectorException("connection_failed", "Could not connect to " + dialect.DisplayName + ": " + e.Message, e);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            DbTransaction? transaction = null;
            if (engineReadOnly)
            {
                try
                {
                    transaction = await dialect.BeginReadOnlyAsync(connection, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is DbException)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw new ConnectorException("connection_failed", "Could not start a read-only transaction on " + dialect.DisplayName + ": " + e.Message, e);
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            return new SqlSession(dialect, options, connection, transaction, engineReadOnly);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a command bound to this session's connection and transaction.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <param name="timeoutSeconds">Driver command timeout in seconds; 0 keeps the connection default.</param>
        /// <returns>Command; the caller disposes it.</returns>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        public DbCommand CreateCommand(string sql, int timeoutSeconds = 0)
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(SqlSession));
            DbCommand command = Connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = Transaction;
            if (timeoutSeconds > 0) command.CommandTimeout = timeoutSeconds;
            return command;
        }

        /// <summary>
        /// Roll back and close.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Roll back and close asynchronously.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (!_Disposed)
            {
                if (Transaction != null)
                {
                    try
                    {
                        await Transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is DbException || e is InvalidOperationException)
                    {
                    }
                    await Transaction.DisposeAsync().ConfigureAwait(false);
                    Transaction = null;
                }
                await Connection.DisposeAsync().ConfigureAwait(false);
                _Disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Roll back and close.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                if (Transaction != null)
                {
                    try
                    {
                        Transaction.Rollback();
                    }
                    catch (Exception e) when (e is DbException || e is InvalidOperationException)
                    {
                    }
                    Transaction.Dispose();
                    Transaction = null;
                }
                Connection.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
