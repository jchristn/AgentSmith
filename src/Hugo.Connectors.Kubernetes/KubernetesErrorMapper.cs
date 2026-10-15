namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Security.Authentication;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s;
    using k8s.Autorest;
    using k8s.Exceptions;
    using k8s.Models;

    /// <summary>
    /// Maps API server responses and client exceptions to <see cref="ConnectorException"/> codes:
    /// <c>auth_failed</c> (401), <c>permission_denied</c> (403), <c>not_found</c> (404), <c>already_exists</c> and
    /// <c>invalid_argument</c> (409), <c>invalid_argument</c> (400, 422), <c>rate_limited</c> (429), <c>timeout</c>
    /// (408, 504, client timeouts), <c>connection_failed</c> (network and TLS failures), and <c>upstream_error</c>.
    /// Messages never contain credential values. Thread-safe.
    /// </summary>
    public static class KubernetesErrorMapper
    {
        #region Public-Methods

        /// <summary>
        /// Map a non-success response.
        /// </summary>
        /// <param name="response">Response.</param>
        /// <param name="target">What was accessed, for example <c>deployments.apps hugo-test/web</c>.</param>
        /// <returns>Connector exception.</returns>
        public static ConnectorException FromResponse(KubernetesApiResponse response, string target)
        {
            ArgumentNullException.ThrowIfNull(response);
            return FromStatus(response.StatusCode, response.Body, target, response.RetryAfterSeconds);
        }

        /// <summary>
        /// Map a status code and body.
        /// </summary>
        /// <param name="status">Status code.</param>
        /// <param name="body">Response body (a Status object when the API server produced it), or null.</param>
        /// <param name="target">What was accessed.</param>
        /// <param name="retryAfterSeconds">Retry-After hint, or null.</param>
        /// <returns>Connector exception.</returns>
        public static ConnectorException FromStatus(HttpStatusCode status, string? body, string target, double? retryAfterSeconds = null)
        {
            V1Status? parsed = ParseStatus(body);
            string serverMessage = parsed?.Message ?? String.Empty;
            string reason = parsed?.Reason ?? String.Empty;
            string detail = String.IsNullOrWhiteSpace(serverMessage) ? String.Empty : " The API server said: " + Shorten(serverMessage, 600);
            int code = (int)status;
            switch (code)
            {
                case 400:
                    return new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The API server rejected the request for " + target + " as invalid." + detail);
                case 401:
                    return new ConnectorException(ConnectorErrorCodes.AuthFailed, "The API server did not accept the credential (401 Unauthorized). Check the kubeconfig or token bound to this resource.");
                case 403:
                    return new ConnectorException(ConnectorErrorCodes.PermissionDenied, "The cluster's RBAC denied access to " + target + "." + detail);
                case 404:
                    return new ConnectorException(ConnectorErrorCodes.NotFound, Capitalize(target) + " was not found." + (String.IsNullOrEmpty(serverMessage) ? String.Empty : " (" + Shorten(serverMessage, 300) + ")"));
                case 405:
                    return new ConnectorException(ConnectorErrorCodes.NotSupported, "The API server does not support this operation on " + target + "." + detail);
                case 408:
                case 504:
                    return new ConnectorException(ConnectorErrorCodes.Timeout, "The API server timed out handling " + target + "." + detail);
                case 409:
                    if (String.Equals(reason, "AlreadyExists", StringComparison.OrdinalIgnoreCase))
                        return new ConnectorException(ConnectorErrorCodes.AlreadyExists, Capitalize(target) + " already exists." + detail);
                    return new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Conflict on " + target + "." + detail + " For server-side apply, another field manager owns these fields; pass force: true only if you mean to take them over.");
                case 410:
                    return new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The continue token or resource version has expired. Start the list again without a continue token.");
                case 413:
                    return new ConnectorException(ConnectorErrorCodes.TooLarge, "The request for " + target + " is too large." + detail);
                case 415:
                    return new ConnectorException(ConnectorErrorCodes.NotSupported, "The API server does not accept this content type for " + target + "." + detail);
                case 422:
                    return new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The API server rejected the object for " + target + " as invalid." + detail);
                case 429:
                    ConnectorException limited = new ConnectorException(ConnectorErrorCodes.RateLimited, "The API server is throttling requests (429)." + detail);
                    limited.RetryAfterSeconds = retryAfterSeconds;
                    return limited;
                default:
                    if (code == 500 && serverMessage.Contains("failed to create typed patch object", StringComparison.Ordinal))
                        return new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The API server rejected the manifest for " + target + " because a field has the wrong type." + detail);
                    if (code >= 500)
                        return new ConnectorException(ConnectorErrorCodes.UpstreamError, "The API server failed handling " + target + " (HTTP " + code + ")." + detail);
                    return new ConnectorException(ConnectorErrorCodes.UpstreamError, "Unexpected HTTP " + code + " for " + target + "." + detail);
            }
        }

        /// <summary>
        /// Map an exception raised while talking to the API server.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <param name="target">What was accessed.</param>
        /// <param name="server">Server URL for messages, or null.</param>
        /// <returns>Connector exception.</returns>
        public static ConnectorException FromException(Exception e, string target, string? server)
        {
            ArgumentNullException.ThrowIfNull(e);
            string where = String.IsNullOrEmpty(server) ? "the API server" : server;
            if (e is ConnectorException ce) return ce;
            if (e is HttpOperationException hoe && hoe.Response != null)
                return FromStatus(hoe.Response.StatusCode, hoe.Response.Content, target);
            if (e is KubeConfigException) return new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The kubeconfig could not be used: " + Shorten(e.Message, 300), e);
            if (e is TimeoutException || e is TaskCanceledException) return new ConnectorException(ConnectorErrorCodes.Timeout, "The request to " + where + " for " + target + " timed out.", e);
            if (e is AuthenticationException) return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The TLS handshake with " + where + " failed. Check the cluster CA certificate and the server URL. (" + Shorten(e.Message, 200) + ")", e);
            if (e is HttpRequestException hre)
            {
                if (hre.InnerException is AuthenticationException) return FromException(hre.InnerException, target, server);
                if (hre.StatusCode.HasValue) return FromStatus(hre.StatusCode.Value, null, target);
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Could not reach " + where + ": " + Shorten(hre.Message, 200), e);
            }
            if (e is SocketException se) return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Could not reach " + where + " (" + se.SocketErrorCode + ").", e);
            if (e is WebSocketException) return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The streaming connection to " + where + " failed: " + Shorten(e.Message, 200), e);
            if (e is KubernetesException ke)
            {
                string message = ke.Status?.Message ?? ke.Message;
                return new ConnectorException(ConnectorErrorCodes.UpstreamError, "The API server reported an error for " + target + ": " + Shorten(message, 400), e);
            }
            if (e is JsonException) return new ConnectorException(ConnectorErrorCodes.UpstreamError, "The API server returned a response for " + target + " that could not be read.", e);
            return new ConnectorException(ConnectorErrorCodes.UpstreamError, "The request for " + target + " failed: " + e.GetType().Name + ".", e);
        }

        /// <summary>
        /// Parse a Status object from a response body, or return null.
        /// </summary>
        /// <param name="body">Body.</param>
        /// <returns>Status, or null.</returns>
        public static V1Status? ParseStatus(string? body)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            string trimmed = body.TrimStart();
            if (!trimmed.StartsWith('{')) return null;
            try
            {
                V1Status status = KubernetesJson.Deserialize<V1Status>(body);
                return String.Equals(status?.Kind, "Status", StringComparison.Ordinal) ? status : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string Shorten(string value, int max)
        {
            string v = (value ?? String.Empty).Replace('\n', ' ').Replace('\r', ' ');
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
