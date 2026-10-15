namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A TCP listener on 127.0.0.1 that accepts connections and never answers, so a client hangs in its handshake. Used
    /// to test timeouts and cancellation without a real server. Thread-safe.
    /// </summary>
    public class SilentTcpServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Listening port.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Connections accepted.
        /// </summary>
        public int Accepted
        {
            get
            {
                return _Accepted;
            }
        }

        #endregion

        #region Private-Members

        private readonly TcpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly ConcurrentBag<TcpClient> _Clients = new ConcurrentBag<TcpClient>();
        private int _Accepted = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening on a free port.
        /// </summary>
        public SilentTcpServer()
        {
            _Listener = new TcpListener(IPAddress.Loopback, 0);
            _Listener.Start();
            Port = ((IPEndPoint)_Listener.LocalEndpoint).Port;
            Task.Run(() => AcceptLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stop listening and close connections.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Stop listening and close connections.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Cts.Cancel();
                _Listener.Stop();
                foreach (TcpClient c in _Clients) c.Dispose();
                _Cts.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _Listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    _Clients.Add(client);
                    Interlocked.Increment(ref _Accepted);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        #endregion
    }
}
