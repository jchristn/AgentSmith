namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Net.Sockets;
    using Hugo.Connectors.Abstractions;
    using Renci.SshNet.Common;

    /// <summary>
    /// Maps SSH.NET and socket exceptions to <see cref="ConnectorException"/> codes: <c>connection_failed</c>,
    /// <c>auth_failed</c>, <c>host_key_mismatch</c>, <c>timeout</c>, <c>not_found</c>, <c>permission_denied</c>,
    /// <c>upstream_error</c>. Messages never contain secret values. Thread-safe.
    /// </summary>
    public static class SshErrorMapper
    {
        /// <summary>
        /// Map an exception raised while connecting.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <param name="endpoint">Endpoint label such as <c>web-01:22</c>.</param>
        /// <param name="check">Host key check used by the attempt, or null.</param>
        /// <returns>Connector exception.</returns>
        public static ConnectorException MapConnect(Exception e, string endpoint, SshHostKeyCheck? check)
        {
            if (e is ConnectorException ce) return ce;
            if (check != null && check.Mismatch)
            {
                return new ConnectorException("host_key_mismatch",
                    "The host key of " + endpoint + " does not match the pinned fingerprint. Expected " + check.Expected + " but the server presented "
                    + (check.Observed ?? "an unknown key") + ". The connection was refused. If the host was legitimately rebuilt, update hostKeyFingerprint on the resource.", e);
            }
            if (e is SshAuthenticationException) return new ConnectorException("auth_failed", "Authentication to " + endpoint + " failed. Check the user name and the bound credential.", e);
            if (e is SshOperationTimeoutException || e is TimeoutException) return new ConnectorException("timeout", "Connecting to " + endpoint + " timed out.", e);
            if (e is SocketException se) return new ConnectorException("connection_failed", "Could not connect to " + endpoint + " (" + se.SocketErrorCode + ").", e);
            if (e is ProxyException) return new ConnectorException("connection_failed", "Could not reach " + endpoint + " through the jump host.", e);
            if (e is SshConnectionException) return new ConnectorException("connection_failed", "The SSH connection to " + endpoint + " failed: " + e.Message, e);
            if (e is SshException) return new ConnectorException("connection_failed", "The SSH connection to " + endpoint + " failed: " + e.Message, e);
            if (e is System.IO.IOException) return new ConnectorException("connection_failed", "The SSH connection to " + endpoint + " failed: " + e.Message, e);
            return new ConnectorException("connection_failed", "The SSH connection to " + endpoint + " failed: " + e.GetType().Name + ".", e);
        }

        /// <summary>
        /// Map an exception raised during an operation on an open session (command or SFTP).
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <param name="target">What was being accessed, for example a path.</param>
        /// <returns>Connector exception.</returns>
        public static ConnectorException MapOperation(Exception e, string target)
        {
            if (e is ConnectorException ce) return ce;
            if (e is SftpPathNotFoundException) return new ConnectorException("not_found", "'" + target + "' was not found.", e);
            if (e is SftpPermissionDeniedException) return new ConnectorException("permission_denied", "Permission denied for '" + target + "'.", e);
            if (e is SshOperationTimeoutException || e is TimeoutException) return new ConnectorException("timeout", "The operation on '" + target + "' timed out.", e);
            if (e is SshConnectionException || e is SocketException || e is System.IO.IOException) return new ConnectorException("connection_failed", "The SSH connection was lost while accessing '" + target + "'.", e);
            if (e is SshException) return new ConnectorException("upstream_error", "The server reported an error for '" + target + "': " + e.Message, e);
            return new ConnectorException("upstream_error", "The operation on '" + target + "' failed: " + e.GetType().Name + ".", e);
        }

        /// <summary>
        /// Whether an exception means the underlying connection is unusable and the pooled session should be dropped.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <returns>True when the session should be invalidated.</returns>
        public static bool IsConnectionBroken(Exception e)
        {
            if (e is ConnectorException ce) return ce.Code == "connection_failed" || (ce.InnerException != null && IsConnectionBroken(ce.InnerException));
            return e is SshConnectionException || e is SocketException || e is ObjectDisposedException || e is System.IO.IOException;
        }
    }
}
