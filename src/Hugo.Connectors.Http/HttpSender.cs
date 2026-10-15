namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Authentication;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Sends one request for a resource, following redirects by hand so that every hop is checked: the target must use
    /// http or https, keep the resource's host, never downgrade from https to http, and stay under the base path and the
    /// allowed prefixes. Authentication is applied only while the origin (scheme, host, port) equals the base URL's.
    /// An OAuth2 request rejected with 401 is retried once with a fresh token. The body is read up to
    /// <see cref="HttpResourceOptions.MaxResponseBytes"/> and the rest is never downloaded. Not thread-safe; use one
    /// instance per call.
    /// </summary>
    public class HttpSender
    {
        #region Private-Members

        private static readonly HashSet<string> _HopByHop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Host", "Content-Length", "Transfer-Encoding", "Connection" };
        private readonly HttpResourceOptions _Options;
        private readonly HttpAuthenticator _Auth;
        private readonly HttpMessageInvoker _Client;
        private readonly IConnectorLogger _Logger;
        private readonly Uri _Base;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Resource options.</param>
        /// <param name="auth">Authenticator for this call.</param>
        /// <param name="client">Client. Null uses <see cref="HttpConnectorClients.For"/> for the resource's TLS mode.</param>
        /// <param name="logger">Logger. Null uses the null logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when options or auth is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when the base URL is unusable.</exception>
        public HttpSender(HttpResourceOptions options, HttpAuthenticator auth, HttpMessageInvoker? client = null, IConnectorLogger? logger = null)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _Auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _Base = options.RequireBaseUri();
            _Client = client ?? HttpConnectorClients.For(options.VerifyTls);
            _Logger = logger ?? NullConnectorLogger.Instance;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the absolute URL of a request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>URL.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        public Uri BuildUrl(HttpOutgoingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            string path = request.EncodedPath == "/" ? _Base.AbsolutePath : _Options.BasePath + request.EncodedPath;
            StringBuilder sb = new StringBuilder(_Base.GetLeftPart(UriPartial.Authority)).Append(path);
            bool first = true;
            foreach (KeyValuePair<string, string> pair in request.Query)
            {
                sb.Append(first ? '?' : '&').Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value ?? String.Empty));
                first = false;
            }
            return new Uri(sb.ToString());
        }

        /// <summary>
        /// Send the request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token including the call timeout.</param>
        /// <returns>Final exchange.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>connection_failed</c> (unreachable or TLS failure) or an authentication code.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public async Task<HttpExchange> SendAsync(HttpOutgoingRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            Stopwatch sw = Stopwatch.StartNew();
            Uri current = BuildUrl(request);
            string method = request.Method;
            byte[]? body = request.Body;
            bool authRetried = false;
            int followed = 0;
            List<HttpRedirectHop> hops = new List<HttpRedirectHop>();

            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool sameOrigin = SameOrigin(current, _Base);
                using (HttpRequestMessage message = new HttpRequestMessage(new HttpMethod(method), current))
                using (ByteArrayContent? content = body == null ? null : new ByteArrayContent(body))
                {
                    foreach (KeyValuePair<string, string> h in _Options.DefaultHeaders) message.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    foreach (KeyValuePair<string, string> h in request.Headers)
                    {
                        message.Headers.Remove(h.Key);
                        message.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    }
                    if (content != null)
                    {
                        content.Headers.TryAddWithoutValidation("Content-Type", String.IsNullOrEmpty(request.ContentType) ? "application/octet-stream" : request.ContentType);
                        message.Content = content;
                    }
                    if (sameOrigin) await _Auth.ApplyAsync(message, token).ConfigureAwait(false);

                    HttpResponseMessage response = await SendOnceAsync(message, current, token).ConfigureAwait(false);
                    using (response)
                    {
                        int status = (int)response.StatusCode;
                        _Logger.Debug("http " + method + " " + current.GetLeftPart(UriPartial.Path) + " returned " + status);

                        if (status == 401 && sameOrigin && _Auth.CanRefresh && !authRetried)
                        {
                            authRetried = true;
                            _Auth.MarkRejected();
                            continue;
                        }

                        if (IsRedirect(status) && response.Headers.Location != null)
                        {
                            Uri target = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                            HttpRedirectHop hop = new HttpRedirectHop { Status = status, From = Display(current), Location = Display(target) };
                            hops.Add(hop);
                            string? refusal = !request.FollowRedirects
                                ? "redirects are disabled for this call"
                                : followed >= _Options.MaxRedirects
                                    ? "the resource's maxRedirects (" + _Options.MaxRedirects + ") was reached"
                                    : RedirectRefusal(current, target);
                            if (refusal == null)
                            {
                                hop.Followed = true;
                                if (!SameOrigin(target, _Base)) hop.Note = "authentication is not sent to a different origin";
                                followed++;
                                if (status == 303 || ((status == 301 || status == 302) && method == "POST"))
                                {
                                    if (method != "HEAD") method = "GET";
                                    body = null;
                                }
                                current = target;
                                continue;
                            }
                            hop.Note = "not followed: " + refusal;
                        }

                        HttpExchange exchange = await ReadAsync(response, method, current, token).ConfigureAwait(false);
                        exchange.Redirects = hops;
                        exchange.AuthRetried = authRetried;
                        exchange.LatencyMs = sw.Elapsed.TotalMilliseconds;
                        return exchange;
                    }
                }
            }
        }

        /// <summary>
        /// Why a redirect target may not be followed, or null when it may.
        /// </summary>
        /// <param name="current">URL that answered with the redirect.</param>
        /// <param name="target">Resolved target.</param>
        /// <returns>Refusal reason or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public string? RedirectRefusal(Uri current, Uri target)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(target);
            if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) return "target scheme '" + target.Scheme + "' is not http or https";
            if (!String.IsNullOrEmpty(target.UserInfo)) return "target URL contains credentials";
            if (!String.Equals(target.Host, _Base.Host, StringComparison.OrdinalIgnoreCase)) return "target host '" + target.Host + "' differs from the resource host '" + _Base.Host + "'";
            if (target.Scheme == Uri.UriSchemeHttp && (current.Scheme == Uri.UriSchemeHttps || _Base.Scheme == Uri.UriSchemeHttps)) return "target would downgrade from https to http";
            if (!HttpPathGuard.TryRelativeToBase(target.AbsolutePath, _Options.BasePath, out string relative)) return "target path '" + target.AbsolutePath + "' is outside the base URL path";
            HttpPathCheck check = HttpPathGuard.Check(relative, _Options.AllowedPathPrefixes);
            if (!check.Allowed) return check.Reason;
            return null;
        }

        /// <summary>
        /// Whether two URLs share scheme, host, and port.
        /// </summary>
        /// <param name="a">First URL.</param>
        /// <param name="b">Second URL.</param>
        /// <returns>True when same origin.</returns>
        public static bool SameOrigin(Uri a, Uri b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            return String.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
                && String.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
                && a.Port == b.Port;
        }

        /// <summary>
        /// Whether a header name is one the transport controls (Host, Content-Length, Transfer-Encoding, Connection).
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>True when reserved.</returns>
        public static bool IsTransportHeader(string name)
        {
            return _HopByHop.Contains(name ?? String.Empty);
        }

        #endregion

        #region Private-Methods

        private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage message, Uri url, CancellationToken token)
        {
            try
            {
                if (_Client is HttpClient client) return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                return await _Client.SendAsync(message, token).ConfigureAwait(false);
            }
            catch (HttpRequestException e) when (FindTlsFailure(e) != null)
            {
                throw new ConnectorException(
                    ConnectorErrorCodes.ConnectionFailed,
                    "The TLS handshake with " + url.Host + ":" + url.Port + " failed: " + FindTlsFailure(e)!.Message + " If the server uses a private CA or a self-signed certificate, the operator can trust it on the host or set verifyTls to false.",
                    e);
            }
            catch (HttpRequestException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, url.Host + ":" + url.Port + " could not be reached: " + e.Message, e);
            }
        }

        private async Task<HttpExchange> ReadAsync(HttpResponseMessage response, string method, Uri url, CancellationToken token)
        {
            HttpExchange exchange = new HttpExchange
            {
                Method = method,
                FinalUrl = Display(url),
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase ?? String.Empty,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                ContentLength = response.Content.Headers.ContentLength
            };
            foreach (KeyValuePair<string, IEnumerable<string>> h in response.Headers.Concat(response.Content.Headers))
            {
                if (String.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string v in h.Value) exchange.Headers.Add(new KeyValuePair<string, string>(h.Key, v));
                }
                else
                {
                    exchange.Headers.Add(new KeyValuePair<string, string>(h.Key, String.Join(", ", h.Value)));
                }
            }

            RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
            if (retry != null)
            {
                if (retry.Delta.HasValue) exchange.RetryAfterSeconds = Math.Max(0, retry.Delta.Value.TotalSeconds);
                else if (retry.Date.HasValue) exchange.RetryAfterSeconds = Math.Max(0, Math.Ceiling((retry.Date.Value - DateTimeOffset.UtcNow).TotalSeconds));
            }

            if (method == "HEAD") return exchange;

            int max = _Options.MaxResponseBytes;
            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[16384];
                while (true)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                    if (read <= 0) break;
                    int room = max - (int)buffer.Length;
                    if (read > room)
                    {
                        buffer.Write(chunk, 0, Math.Max(0, room));
                        exchange.BodyTruncated = true;
                        break;
                    }
                    buffer.Write(chunk, 0, read);
                }
                exchange.Body = buffer.ToArray();
            }
            return exchange;
        }

        private static AuthenticationException? FindTlsFailure(Exception e)
        {
            Exception? cursor = e;
            while (cursor != null)
            {
                if (cursor is AuthenticationException auth) return auth;
                cursor = cursor.InnerException;
            }
            return null;
        }

        private static bool IsRedirect(int status)
        {
            return status == 301 || status == 302 || status == 303 || status == 307 || status == 308;
        }

        private static string Display(Uri uri)
        {
            if (String.IsNullOrEmpty(uri.UserInfo)) return uri.AbsoluteUri;
            UriBuilder b = new UriBuilder(uri) { UserName = String.Empty, Password = String.Empty };
            return b.Uri.AbsoluteUri;
        }

        #endregion
    }
}
