namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Renci.SshNet;

    /// <summary>
    /// A connected SSH session to one resource: the target client, an optional bastion client with the local port
    /// forward that tunnels to the target, and a lazily opened SFTP client. Sessions are pooled through
    /// <c>IConnectorSessionPool</c>; callers must not dispose a pooled session. Thread-safe: SSH.NET multiplexes
    /// concurrent channels, and SFTP creation is serialized.
    /// </summary>
    public class SshSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Connected client for the target.
        /// </summary>
        public SshClient Client { get; }

        /// <summary>
        /// Fingerprint the target presented (<c>SHA256:...</c>).
        /// </summary>
        public string HostKeyFingerprint { get; }

        /// <summary>
        /// Host key algorithm the target presented.
        /// </summary>
        public string HostKeyAlgorithm { get; }

        /// <summary>
        /// Fingerprint the bastion presented, or null without a bastion.
        /// </summary>
        public string? JumpHostKeyFingerprint { get; }

        /// <summary>
        /// Server identification string of the target (for example <c>SSH-2.0-OpenSSH_10.3</c>).
        /// </summary>
        public string ServerVersion
        {
            get
            {
                return Client.ConnectionInfo?.ServerVersion ?? String.Empty;
            }
        }

        /// <summary>
        /// Whether the target (and bastion, when used) are still connected.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (_Disposed) return false;
                if (!Client.IsConnected) return false;
                if (_JumpClient != null && !_JumpClient.IsConnected) return false;
                return true;
            }
        }

        /// <summary>
        /// Options the session was opened with.
        /// </summary>
        public SshConnectionOptions Options { get; }

        #endregion

        #region Private-Members

        private readonly SshCredential _Credential;
        private readonly SshClient? _JumpClient;
        private readonly SshCredential? _JumpCredential;
        private readonly ForwardedPortLocal? _Forward;
        private readonly string _ConnectHost;
        private readonly int _ConnectPort;
        private readonly SemaphoreSlim _SftpLock = new SemaphoreSlim(1, 1);
        private SftpClient? _Sftp = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. Ownership of every disposable argument passes to the session.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="client">Connected target client.</param>
        /// <param name="credential">Target credential, reused for SFTP.</param>
        /// <param name="connectHost">Host the target client connected to (127.0.0.1 when tunneled).</param>
        /// <param name="connectPort">Port the target client connected to.</param>
        /// <param name="hostKeyFingerprint">Target fingerprint.</param>
        /// <param name="hostKeyAlgorithm">Target host key algorithm.</param>
        /// <param name="jumpClient">Connected bastion client, or null.</param>
        /// <param name="jumpCredential">Bastion credential, or null.</param>
        /// <param name="forward">Started local forward through the bastion, or null.</param>
        /// <param name="jumpHostKeyFingerprint">Bastion fingerprint, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SshSession(
            SshConnectionOptions options,
            SshClient client,
            SshCredential credential,
            string connectHost,
            int connectPort,
            string hostKeyFingerprint,
            string hostKeyAlgorithm,
            SshClient? jumpClient,
            SshCredential? jumpCredential,
            ForwardedPortLocal? forward,
            string? jumpHostKeyFingerprint)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Client = client ?? throw new ArgumentNullException(nameof(client));
            _Credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _ConnectHost = connectHost ?? throw new ArgumentNullException(nameof(connectHost));
            _ConnectPort = connectPort;
            HostKeyFingerprint = hostKeyFingerprint ?? String.Empty;
            HostKeyAlgorithm = hostKeyAlgorithm ?? String.Empty;
            _JumpClient = jumpClient;
            _JumpCredential = jumpCredential;
            _Forward = forward;
            JumpHostKeyFingerprint = jumpHostKeyFingerprint;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the SFTP client, connecting it on first use. The SFTP connection verifies the host key against the
        /// fingerprint this session already accepted.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Connected SFTP client. Callers must not dispose it.</returns>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        /// <exception cref="Hugo.Connectors.Abstractions.ConnectorException">Thrown when the SFTP connection fails.</exception>
        public async Task<SftpClient> GetSftpAsync(CancellationToken token = default)
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(SshSession));
            await _SftpLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Sftp != null && _Sftp.IsConnected) return _Sftp;
                if (_Sftp != null)
                {
                    _Sftp.Dispose();
                    _Sftp = null;
                }

                SshHostKeyCheck check = new SshHostKeyCheck(HostKeyFingerprint);
                ConnectionInfo info = _Credential.BuildConnectionInfo(_ConnectHost, _ConnectPort, TimeSpan.FromSeconds(Options.ConnectTimeoutSeconds));
                SftpClient sftp = new SftpClient(info);
                sftp.OperationTimeout = TimeSpan.FromSeconds(Options.CommandTimeoutSeconds);
                await SshSessionFactory.ConnectClientAsync(sftp, check, Options.Host + ":" + Options.Port, Options, token).ConfigureAwait(false);
                _Sftp = sftp;
                return sftp;
            }
            finally
            {
                _SftpLock.Release();
            }
        }

        /// <summary>
        /// Disconnect and release every client, the forward, and the credentials.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Disconnect and release resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (!disposing) return;

            SafeDispose(_Sftp);
            SafeDispose(Client);
            if (_Forward != null)
            {
                try
                {
                    if (_Forward.IsStarted) _Forward.Stop();
                }
                catch (Exception)
                {
                }
                SafeDispose(_Forward);
            }
            SafeDispose(_JumpClient);
            SafeDispose(_Credential);
            SafeDispose(_JumpCredential);
            _SftpLock.Dispose();
        }

        #endregion

        #region Private-Methods

        private static void SafeDispose(IDisposable? d)
        {
            if (d == null) return;
            try
            {
                d.Dispose();
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}
