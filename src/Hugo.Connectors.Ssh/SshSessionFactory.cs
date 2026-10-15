namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Renci.SshNet;

    /// <summary>
    /// Opens SSH sessions: authenticates, verifies host keys (pinning on first use per D-028), and chains through a
    /// bastion with a local port forward when <c>jumpHost</c> is set. Thread-safe.
    /// </summary>
    public static class SshSessionFactory
    {
        #region Public-Methods

        /// <summary>
        /// Open a new session for a resource.
        /// </summary>
        /// <param name="resource">Resource (settings, secrets, logger, learn-setting recorder).</param>
        /// <param name="options">Parsed options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Connected session. The caller owns it (or hands it to the pool).</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c>, <c>connection_failed</c>, <c>auth_failed</c>, <c>host_key_mismatch</c>, or <c>timeout</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public static async Task<SshSession> ConnectAsync(ConnectorResourceContext resource, SshConnectionOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(options);
            if (String.IsNullOrWhiteSpace(options.Host)) throw new ConnectorException("invalid_argument", "The resource has no host configured.");
            if (String.IsNullOrWhiteSpace(options.Username)) throw new ConnectorException("invalid_argument", "The resource has no username configured.");
            token.ThrowIfCancellationRequested();

            TimeSpan timeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds);
            string targetEndpoint = options.Host + ":" + options.Port;
            SshCredential? credential = null;
            SshCredential? jumpCredential = null;
            SshClient? jumpClient = null;
            SshClient? client = null;
            ForwardedPortLocal? forward = null;

            try
            {
                credential = SshCredential.FromSecrets(resource.Secrets, options.Username, SshSettingKeys.PasswordSlot, SshSettingKeys.PrivateKeySlot, options.AuthMethod, "target " + targetEndpoint);

                string connectHost = options.Host;
                int connectPort = options.Port;
                string? jumpFingerprint = null;

                if (options.UsesJumpHost && options.JumpHost != null)
                {
                    string jumpEndpoint = options.JumpHost + ":" + options.JumpPort;
                    string jumpUser = String.IsNullOrWhiteSpace(options.JumpUsername) ? options.Username : options.JumpUsername;
                    jumpCredential = SshCredential.FromSecrets(resource.Secrets, jumpUser, SshSettingKeys.JumpPasswordSlot, SshSettingKeys.JumpPrivateKeySlot, null, "jump host " + jumpEndpoint);
                    SshHostKeyCheck jumpCheck = new SshHostKeyCheck(options.JumpHostKeyFingerprint);
                    jumpClient = new SshClient(jumpCredential.BuildConnectionInfo(options.JumpHost, options.JumpPort, timeout));
                    await ConnectClientAsync(jumpClient, jumpCheck, "jump host " + jumpEndpoint, options, token).ConfigureAwait(false);
                    jumpFingerprint = jumpCheck.Observed;
                    Learn(resource, SshSettingKeys.JumpHostKeyFingerprint, jumpCheck, "jump host " + jumpEndpoint);

                    forward = new ForwardedPortLocal("127.0.0.1", 0, options.Host, (uint)options.Port);
                    jumpClient.AddForwardedPort(forward);
                    try
                    {
                        forward.Start();
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        throw new ConnectorException("connection_failed", "Could not open a tunnel to " + targetEndpoint + " through jump host " + jumpEndpoint + ". Check that the jump host allows TCP forwarding.", e);
                    }
                    connectHost = "127.0.0.1";
                    connectPort = (int)forward.BoundPort;
                    targetEndpoint = targetEndpoint + " (via " + jumpEndpoint + ")";
                }

                SshHostKeyCheck check = new SshHostKeyCheck(options.HostKeyFingerprint);
                client = new SshClient(credential.BuildConnectionInfo(connectHost, connectPort, timeout));
                await ConnectClientAsync(client, check, targetEndpoint, options, token).ConfigureAwait(false);
                Learn(resource, SshSettingKeys.HostKeyFingerprint, check, targetEndpoint);

                SshSession session = new SshSession(options, client, credential, connectHost, connectPort, check.Observed ?? String.Empty, check.Algorithm ?? String.Empty, jumpClient, jumpCredential, forward, jumpFingerprint);
                resource.Logger.Debug("ssh: connected to " + targetEndpoint + " as " + options.Username);
                return session;
            }
            catch (Exception)
            {
                SafeDispose(client);
                if (forward != null)
                {
                    try
                    {
                        if (forward.IsStarted) forward.Stop();
                    }
                    catch (Exception)
                    {
                    }
                    SafeDispose(forward);
                }
                SafeDispose(jumpClient);
                SafeDispose(credential);
                SafeDispose(jumpCredential);
                throw;
            }
        }

        /// <summary>
        /// Connect an SSH or SFTP client with host key verification, keepalive, and a connect timeout.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="check">Host key check.</param>
        /// <param name="endpoint">Endpoint label for messages.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ConnectorException">Thrown when the connection fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public static async Task ConnectClientAsync(BaseClient client, SshHostKeyCheck check, string endpoint, SshConnectionOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(check);
            ArgumentNullException.ThrowIfNull(options);

            client.HostKeyReceived += check.OnHostKeyReceived;
            if (options.KeepAliveSeconds > 0) client.KeepAliveInterval = TimeSpan.FromSeconds(options.KeepAliveSeconds);

            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
                try
                {
                    await client.ConnectAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    if (check.Mismatch) throw SshErrorMapper.MapConnect(new TimeoutException(), endpoint, check);
                    throw new ConnectorException("timeout", "Connecting to " + endpoint + " timed out after " + options.ConnectTimeoutSeconds + " seconds.");
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    throw SshErrorMapper.MapConnect(e, endpoint, check);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void Learn(ConnectorResourceContext resource, string key, SshHostKeyCheck check, string endpoint)
        {
            if (check.Expected != null || check.Observed == null) return;
            resource.Logger.Info("ssh: trusting host key " + check.Observed + " (" + check.Algorithm + ") of " + endpoint + " on first use for resource '" + resource.Name + "'; pinning it as " + key + ".");
            resource.LearnSetting?.Invoke(key, check.Observed);
        }

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
