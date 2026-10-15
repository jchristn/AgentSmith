namespace Hugo.Connectors.Redis
{
    using System;
    using System.IO;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// Maps StackExchange.Redis and socket exceptions to <see cref="ConnectorException"/> with stable codes. Messages never
    /// contain the password. Thread-safe.
    /// </summary>
    public static class RedisErrorMapper
    {
        #region Public-Members

        /// <summary>
        /// Maximum characters of an upstream message included in an error. Minimum 64, maximum 4096, default 600.
        /// </summary>
        public static int MaxMessageChars
        {
            get
            {
                return _MaxMessageChars;
            }
            set
            {
                _MaxMessageChars = Math.Clamp(value, 64, 4096);
            }
        }

        #endregion

        #region Private-Members

        private static int _MaxMessageChars = 600;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Map an exception raised while running an operation.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <param name="operation">Operation description for the message (for example <c>HGETALL</c>).</param>
        /// <returns>Mapped exception.</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static ConnectorException Map(Exception e, string operation)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e is ConnectorException ce) return ce;
            string what = String.IsNullOrWhiteSpace(operation) ? "Redis call" : operation;

            if (e is RedisConnectionException rce)
            {
                if (rce.FailureType == ConnectionFailureType.AuthenticationFailure || IsAuthMessage(FullMessage(rce)))
                    return new ConnectorException(ConnectorErrorCodes.AuthFailed, "Redis rejected the credentials during " + what + ". Check the password secret and the username setting (ACL user).", e);
                if (FullMessage(rce).Contains("AuthenticationException", StringComparison.OrdinalIgnoreCase) || rce.InnerException is AuthenticationException)
                    return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The TLS handshake with Redis failed during " + what + ". Check useSsl, sslHost, verifyTls, and caCertificate.", e);
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Redis could not be reached during " + what + " (" + rce.FailureType + "): " + Shorten(rce.Message), e);
            }

            if (e is RedisTimeoutException)
                return new ConnectorException(ConnectorErrorCodes.Timeout, "Redis did not answer " + what + " within the command timeout.", e);

            if (e is RedisServerException rse) return MapServerError(rse.Message, what, e);

            if (e is RedisCommandException)
                return new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The Redis client refused " + what + ": " + Shorten(e.Message), e);

            if (e is AuthenticationException)
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The TLS handshake with Redis failed during " + what + ": " + Shorten(e.Message), e);

            if (e is SocketException || e is IOException)
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The connection to Redis failed during " + what + ": " + Shorten(e.Message), e);

            if (e is RedisException)
                return new ConnectorException(ConnectorErrorCodes.UpstreamError, "Redis failed during " + what + ": " + Shorten(e.Message), e);

            return new ConnectorException(ConnectorErrorCodes.UpstreamError, "Unexpected error during " + what + ": " + Shorten(e.Message), e);
        }

        /// <summary>
        /// Map a Redis error reply (for example <c>WRONGTYPE ...</c>) to a code.
        /// </summary>
        /// <param name="message">Error reply text.</param>
        /// <param name="operation">Operation description.</param>
        /// <param name="inner">Inner exception, or null.</param>
        /// <returns>Mapped exception.</returns>
        public static ConnectorException MapServerError(string? message, string operation, Exception? inner)
        {
            string text = (message ?? String.Empty).Trim();
            string upper = text.ToUpperInvariant();
            string code;
            if (IsAuthMessage(text)) code = ConnectorErrorCodes.AuthFailed;
            else if (upper.StartsWith("NOPERM", StringComparison.Ordinal) || upper.StartsWith("READONLY", StringComparison.Ordinal)) code = ConnectorErrorCodes.PermissionDenied;
            else if (upper.StartsWith("WRONGTYPE", StringComparison.Ordinal) || upper.StartsWith("CROSSSLOT", StringComparison.Ordinal) || upper.StartsWith("MOVED", StringComparison.Ordinal)
                || upper.StartsWith("ERR UNKNOWN", StringComparison.Ordinal) || upper.StartsWith("ERR WRONG NUMBER", StringComparison.Ordinal)
                || upper.StartsWith("ERR SYNTAX", StringComparison.Ordinal) || upper.Contains("NOT AN INTEGER", StringComparison.Ordinal)
                || upper.Contains("NOT A VALID FLOAT", StringComparison.Ordinal) || upper.StartsWith("ERR INVALID", StringComparison.Ordinal)) code = ConnectorErrorCodes.InvalidArgument;
            else if (upper.StartsWith("BUSY", StringComparison.Ordinal) || upper.StartsWith("LOADING", StringComparison.Ordinal) || upper.StartsWith("TRYAGAIN", StringComparison.Ordinal)
                || upper.StartsWith("MASTERDOWN", StringComparison.Ordinal) || upper.StartsWith("CLUSTERDOWN", StringComparison.Ordinal)) code = ConnectorErrorCodes.UpstreamError;
            else code = ConnectorErrorCodes.UpstreamError;
            string msg = "Redis returned an error for " + operation + ": " + Shorten(text);
            return inner == null ? new ConnectorException(code, msg) : new ConnectorException(code, msg, inner);
        }

        #endregion

        #region Private-Methods

        private static bool IsAuthMessage(string text)
        {
            string upper = (text ?? String.Empty).ToUpperInvariant();
            return upper.Contains("WRONGPASS", StringComparison.Ordinal)
                || upper.Contains("NOAUTH", StringComparison.Ordinal)
                || upper.Contains("INVALID USERNAME-PASSWORD", StringComparison.Ordinal)
                || upper.Contains("INVALID PASSWORD", StringComparison.Ordinal)
                || upper.Contains("AUTHENTICATIONFAILURE", StringComparison.Ordinal)
                || upper.Contains("AUTH FAILED", StringComparison.Ordinal);
        }

        private static string FullMessage(Exception e)
        {
            string text = e.Message;
            Exception? inner = e.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                text += " " + inner.GetType().Name + ": " + inner.Message;
                inner = inner.InnerException;
                depth++;
            }
            return text;
        }

        private static string Shorten(string? text)
        {
            string v = (text ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return v.Length > _MaxMessageChars ? v.Substring(0, _MaxMessageChars) + "..." : v;
        }

        #endregion
    }
}
