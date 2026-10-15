namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s;

    /// <summary>
    /// A pooled connection to one cluster: the KubernetesClient instance, the server URL, and a discovery cache. Raw REST
    /// calls go through <see cref="SendAsync"/>, which applies the resource's credential and bounds the response size.
    /// Thread-safe.
    /// </summary>
    public class KubernetesSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The client. Do not dispose; the session owns it.
        /// </summary>
        public Kubernetes Client { get; }

        /// <summary>
        /// API server URL, without a trailing slash.
        /// </summary>
        public string Server { get; }

        /// <summary>
        /// Discovery cache for this cluster.
        /// </summary>
        public KubernetesDiscovery Discovery { get; }

        /// <summary>
        /// Largest response body read, in bytes. Minimum 64 KiB, maximum 256 MiB, default 32 MiB. Larger responses fail
        /// with <c>too_large</c>.
        /// </summary>
        public int MaxResponseBytes
        {
            get
            {
                return _MaxResponseBytes;
            }
            set
            {
                _MaxResponseBytes = Math.Clamp(value, 64 * 1024, 256 * 1024 * 1024);
            }
        }

        /// <summary>
        /// Timeout of one request. Minimum 1 second, maximum 10 minutes, default 30 seconds.
        /// </summary>
        public TimeSpan RequestTimeout
        {
            get
            {
                return _RequestTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(10)) value = TimeSpan.FromMinutes(10);
                _RequestTimeout = value;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _RequestTimeout = TimeSpan.FromSeconds(30);
        private int _MaxResponseBytes = 32 * 1024 * 1024;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="client">Client; the session takes ownership.</param>
        /// <param name="server">Server URL.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public KubernetesSession(Kubernetes client, string server)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Server = (server ?? String.Empty).TrimEnd('/');
            Discovery = new KubernetesDiscovery(this);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a raw REST request.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="pathAndQuery">Path starting with <c>/</c>, with an optional query string.</param>
        /// <param name="body">Body text, or null.</param>
        /// <param name="contentType">Body content type, or null for JSON.</param>
        /// <param name="accept">Accept header, or null for JSON.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response; non-success statuses are returned, not thrown.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>too_large</c> when the body exceeds <see cref="MaxResponseBytes"/>,
        /// <c>timeout</c> when <see cref="RequestTimeout"/> elapses, and a mapped code for network and TLS failures.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the caller's token is cancelled.</exception>
        public async Task<KubernetesApiResponse> SendAsync(HttpMethod method, string pathAndQuery, string? body, string? contentType, string? accept, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(method);
            ObjectDisposedException.ThrowIf(_Disposed, this);
            if (String.IsNullOrEmpty(pathAndQuery) || pathAndQuery[0] != '/') throw new ArgumentException("The path must start with '/'.", nameof(pathAndQuery));
            token.ThrowIfCancellationRequested();

            string baseUrl = Client.BaseUri.ToString().TrimEnd('/');
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (HttpRequestMessage request = new HttpRequestMessage(method, new Uri(baseUrl + pathAndQuery)))
            {
                cts.CancelAfter(_RequestTimeout);
                try
                {
                    request.Headers.Accept.Clear();
                    request.Headers.TryAddWithoutValidation("Accept", String.IsNullOrEmpty(accept) ? "application/json" : accept);
                    if (body != null)
                    {
                        request.Content = new StringContent(body, Encoding.UTF8);
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(String.IsNullOrEmpty(contentType) ? "application/json" : contentType);
                    }
                    if (Client.Credentials != null) await Client.Credentials.ProcessHttpRequestAsync(request, cts.Token).ConfigureAwait(false);

                    using (HttpResponseMessage response = await Client.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                    {
                        KubernetesApiResponse result = new KubernetesApiResponse();
                        result.StatusCode = response.StatusCode;
                        if (response.Headers.RetryAfter != null)
                        {
                            if (response.Headers.RetryAfter.Delta.HasValue) result.RetryAfterSeconds = response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                            else if (response.Headers.RetryAfter.Date.HasValue) result.RetryAfterSeconds = Math.Max(0, (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
                        }
                        result.Body = await ReadBoundedAsync(response, pathAndQuery, cts.Token).ConfigureAwait(false);
                        return result;
                    }
                }
                catch (OperationCanceledException e) when (!token.IsCancellationRequested)
                {
                    throw new ConnectorException(ConnectorErrorCodes.Timeout, "The API server did not answer " + method.Method + " " + Strip(pathAndQuery) + " within " + (int)_RequestTimeout.TotalSeconds + " seconds (requestTimeoutSeconds).", e);
                }
                catch (Exception e) when (!(e is OperationCanceledException) && !(e is ConnectorException))
                {
                    throw KubernetesErrorMapper.FromException(e, Strip(pathAndQuery), Server);
                }
            }
        }

        /// <summary>
        /// GET a path and return the body, throwing a mapped exception on failure.
        /// </summary>
        /// <param name="pathAndQuery">Path and query.</param>
        /// <param name="target">What is being read, for error messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="accept">Accept header, or null for JSON.</param>
        /// <returns>Body.</returns>
        /// <exception cref="ConnectorException">Thrown when the request fails.</exception>
        public async Task<string> GetAsync(string pathAndQuery, string target, CancellationToken token, string? accept = null)
        {
            KubernetesApiResponse response = await SendAsync(HttpMethod.Get, pathAndQuery, null, null, accept, token).ConfigureAwait(false);
            if (!response.IsSuccess) throw KubernetesErrorMapper.FromResponse(response, target);
            return response.Body;
        }

        /// <summary>
        /// Send a request and return the body, throwing a mapped exception on failure.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="pathAndQuery">Path and query.</param>
        /// <param name="body">Body, or null.</param>
        /// <param name="contentType">Content type, or null for JSON.</param>
        /// <param name="target">What is being changed, for error messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Body.</returns>
        /// <exception cref="ConnectorException">Thrown when the request fails.</exception>
        public async Task<string> SendCheckedAsync(HttpMethod method, string pathAndQuery, string? body, string? contentType, string target, CancellationToken token)
        {
            KubernetesApiResponse response = await SendAsync(method, pathAndQuery, body, contentType, null, token).ConfigureAwait(false);
            if (!response.IsSuccess) throw KubernetesErrorMapper.FromResponse(response, target);
            return response.Body;
        }

        /// <summary>
        /// Build a query string from name and value pairs, skipping null or empty values.
        /// </summary>
        /// <param name="pairs">Alternating names and values.</param>
        /// <returns>Query string starting with <c>?</c>, or empty.</returns>
        public static string Query(params string?[] pairs)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                string? name = pairs[i];
                string? value = pairs[i + 1];
                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(value)) continue;
                sb.Append(sb.Length == 0 ? '?' : '&');
                sb.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Format an integer for a query string.
        /// </summary>
        /// <param name="value">Value, or null.</param>
        /// <returns>Text, or null.</returns>
        public static string? Number(long? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// Dispose the client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose the client.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) Client.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<string> ReadBoundedAsync(HttpResponseMessage response, string path, CancellationToken token)
        {
            long? declared = response.Content.Headers.ContentLength;
            if (declared.HasValue && declared.Value > _MaxResponseBytes)
                throw new ConnectorException(ConnectorErrorCodes.TooLarge, "The response for " + Strip(path) + " is " + declared.Value + " bytes, more than the " + _MaxResponseBytes + " byte limit. Narrow the request with a selector or a limit.");
            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                while (true)
                {
                    int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                    if (read <= 0) break;
                    if (buffer.Length + read > _MaxResponseBytes)
                        throw new ConnectorException(ConnectorErrorCodes.TooLarge, "The response for " + Strip(path) + " is larger than the " + _MaxResponseBytes + " byte limit. Narrow the request with a selector or a limit.");
                    buffer.Write(chunk, 0, read);
                }
                return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }

        private static string Strip(string path)
        {
            int q = path.IndexOf('?');
            return q < 0 ? path : path.Substring(0, q);
        }

        #endregion
    }
}
