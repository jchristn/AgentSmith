namespace Hugo.Connectors.Docker
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Text.Json;
    using System.Threading.Tasks;
    using global::Docker.DotNet;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Maps Docker.DotNet and network exceptions to <see cref="ConnectorException"/> codes: <c>not_found</c>,
    /// <c>invalid_argument</c> (409 conflicts such as removing a running container), <c>permission_denied</c>,
    /// <c>auth_failed</c>, <c>timeout</c>, <c>connection_failed</c>, and <c>upstream_error</c>. Thread-safe.
    /// </summary>
    public static class DockerErrorMapper
    {
        #region Public-Methods

        /// <summary>
        /// Map an exception.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <param name="target">What was accessed, for example <c>container web</c>.</param>
        /// <param name="endpoint">Daemon endpoint for messages.</param>
        /// <returns>Connector exception.</returns>
        public static ConnectorException Map(Exception e, string target, string endpoint)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e is ConnectorException ce) return ce;
            if (e is AggregateException ae && ae.InnerExceptions.Count >= 1) return Map(ae.InnerExceptions[0], target, endpoint);
            if (e is DockerContainerNotFoundException || e is DockerImageNotFoundException)
                return new ConnectorException(ConnectorErrorCodes.NotFound, Capitalize(target) + " was not found.", e);
            if (e is DockerApiException api) return FromStatus(api.StatusCode, MessageOf(api.ResponseBody), target, e);
            if (e is TimeoutException || e is TaskCanceledException)
                return new ConnectorException(ConnectorErrorCodes.Timeout, "The Docker daemon at " + endpoint + " did not answer in time for " + target + ".", e);
            if (e is AuthenticationException)
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The TLS handshake with " + endpoint + " failed. Check the client certificate, the CA, and tlsVerify. (" + e.Message + ")", e);
            if (e is HttpRequestException hre)
            {
                if (hre.InnerException != null && !(hre.InnerException is HttpRequestException)) return Map(hre.InnerException, target, endpoint);
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Could not reach the Docker daemon at " + endpoint + ": " + hre.Message, e);
            }
            if (e is SocketException se)
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Could not reach the Docker daemon at " + endpoint + " (" + se.SocketErrorCode + ").", e);
            if (e is IOException)
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The connection to the Docker daemon at " + endpoint + " failed: " + e.Message, e);
            if (e is JsonException)
                return new ConnectorException(ConnectorErrorCodes.UpstreamError, "The Docker daemon returned a response for " + target + " that could not be read.", e);
            return new ConnectorException(ConnectorErrorCodes.UpstreamError, "The request for " + target + " failed: " + e.GetType().Name + ".", e);
        }

        /// <summary>
        /// Map an HTTP status from the daemon.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="message">Daemon message, or null.</param>
        /// <param name="target">What was accessed.</param>
        /// <param name="inner">Inner exception, or null.</param>
        /// <returns>Connector exception.</returns>
        public static ConnectorException FromStatus(HttpStatusCode status, string? message, string target, Exception? inner = null)
        {
            string detail = String.IsNullOrWhiteSpace(message) ? String.Empty : " The daemon said: " + Shorten(message, 400);
            int code = (int)status;
            string text;
            string mapped;
            switch (code)
            {
                case 400:
                    mapped = ConnectorErrorCodes.InvalidArgument;
                    text = "The daemon rejected the request for " + target + "." + detail;
                    break;
                case 401:
                    mapped = ConnectorErrorCodes.AuthFailed;
                    text = "The daemon did not accept the credential." + detail;
                    break;
                case 403:
                    mapped = ConnectorErrorCodes.PermissionDenied;
                    text = "The daemon refused access to " + target + "." + detail;
                    break;
                case 404:
                    mapped = ConnectorErrorCodes.NotFound;
                    text = Capitalize(target) + " was not found." + detail;
                    break;
                case 409:
                    mapped = ConnectorErrorCodes.InvalidArgument;
                    text = "Conflict for " + target + "." + detail;
                    break;
                case 304:
                    mapped = ConnectorErrorCodes.InvalidArgument;
                    text = Capitalize(target) + " is already in that state.";
                    break;
                default:
                    mapped = ConnectorErrorCodes.UpstreamError;
                    text = "The daemon failed handling " + target + " (HTTP " + code + ")." + detail;
                    break;
            }
            return inner == null ? new ConnectorException(mapped, text) : new ConnectorException(mapped, text, inner);
        }

        /// <summary>
        /// Whether an exception means the connection is unusable and the pooled client should be dropped.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <returns>True when the session should be invalidated.</returns>
        public static bool IsConnectionBroken(Exception e)
        {
            if (e is ConnectorException ce) return ce.Code == ConnectorErrorCodes.ConnectionFailed;
            if (e is AggregateException ae && ae.InnerExceptions.Count >= 1) return IsConnectionBroken(ae.InnerExceptions[0]);
            return e is HttpRequestException || e is SocketException || e is IOException || e is ObjectDisposedException;
        }

        #endregion

        #region Private-Methods

        private static string? MessageOf(string? body)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            string trimmed = body.Trim();
            if (trimmed.StartsWith('{'))
            {
                try
                {
                    DockerErrorBody? parsed = JsonSerializer.Deserialize<DockerErrorBody>(trimmed);
                    if (!String.IsNullOrEmpty(parsed?.Message)) return parsed.Message;
                }
                catch (JsonException)
                {
                    return trimmed;
                }
            }
            return trimmed;
        }

        private static string Shorten(string value, int max)
        {
            string v = value.Replace('\n', ' ').Replace('\r', ' ');
            return v.Length > max ? v.Substring(0, max - 3) + "..." : v;
        }

        private static string Capitalize(string value)
        {
            if (String.IsNullOrEmpty(value)) return "The object";
            return Char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        #endregion
    }
}
