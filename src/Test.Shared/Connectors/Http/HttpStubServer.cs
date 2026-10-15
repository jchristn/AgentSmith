namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal in-process HTTP/1.1 server on 127.0.0.1 and a free port, built on <see cref="TcpListener"/>, with optional
    /// TLS from a self-signed certificate created at run time. Every response closes the connection. It records
    /// requests, can delay responses (timeouts and cancellation), and serves an OAuth2 client credentials token endpoint
    /// that counts issued tokens and can revoke them. Thread-safe.
    /// </summary>
    public class HttpStubServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base address, for example <c>https://127.0.0.1:50123</c>.
        /// </summary>
        public string BaseAddress { get; }

        /// <summary>
        /// Port.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Server certificate, or null for plain HTTP.
        /// </summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>
        /// Handler for every request except the token endpoint. Default answers 200 "ok".
        /// </summary>
        public Func<HttpStubRequest, HttpStubResponse> Handler { get; set; } = r => HttpStubResponse.Text(200, "ok");

        /// <summary>
        /// Delay before each response, in milliseconds. Default 0.
        /// </summary>
        public int DelayMilliseconds { get; set; } = 0;

        /// <summary>
        /// Requests received, in order (token endpoint included).
        /// </summary>
        public ConcurrentQueue<HttpStubRequest> Requests { get; } = new ConcurrentQueue<HttpStubRequest>();

        /// <summary>
        /// Path of the OAuth2 token endpoint.
        /// </summary>
        public string TokenPath { get; set; } = "/oauth/token";

        /// <summary>
        /// Accepted client ID.
        /// </summary>
        public string ClientId { get; set; } = "stub-client";

        /// <summary>
        /// Accepted client secret.
        /// </summary>
        public string ClientSecret { get; set; } = "stub-client-secret-value";

        /// <summary>
        /// expires_in reported for issued tokens. Default 3600.
        /// </summary>
        public int TokenLifetimeSeconds { get; set; } = 3600;

        /// <summary>
        /// Number of tokens issued.
        /// </summary>
        public int TokensIssued
        {
            get
            {
                return _TokensIssued;
            }
        }

        /// <summary>
        /// Scope sent with the last token request, or null.
        /// </summary>
        public string? LastTokenScope { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly TcpListener _Listener;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, bool> _Tokens = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private int _TokensIssued = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening.
        /// </summary>
        /// <param name="useTls">Serve HTTPS with a self-signed certificate.</param>
        /// <param name="certificateDays">Days until the certificate expires.</param>
        public HttpStubServer(bool useTls = false, int certificateDays = 30)
        {
            _Listener = new TcpListener(IPAddress.Loopback, 0);
            _Listener.Start();
            Port = ((IPEndPoint)_Listener.LocalEndpoint).Port;
            if (useTls) Certificate = HttpStubCertificate.Create(certificateDays);
            BaseAddress = (useTls ? "https" : "http") + "://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);
            Task.Run(() => AcceptLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Revoke every issued token (the next API call with one gets 401).
        /// </summary>
        public void RevokeTokens()
        {
            _Tokens.Clear();
        }

        /// <summary>
        /// Whether a request carries a valid issued bearer token.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>True when valid.</returns>
        public bool HasValidToken(HttpStubRequest request)
        {
            string? auth = request.Header("Authorization");
            if (auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
            return _Tokens.ContainsKey(auth.Substring(7));
        }

        /// <summary>
        /// Requests to a path, in order.
        /// </summary>
        /// <param name="path">Raw path.</param>
        /// <returns>Requests.</returns>
        public List<HttpStubRequest> To(string path)
        {
            List<HttpStubRequest> list = new List<HttpStubRequest>();
            foreach (HttpStubRequest r in Requests) if (r.Path == path) list.Add(r);
            return list;
        }

        /// <summary>
        /// Stop listening.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Stop listening.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Cts.Cancel();
                _Listener.Stop();
                Certificate?.Dispose();
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
                TcpClient client;
                try
                {
                    client = await _Listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                _ = Task.Run(() => ServeAsync(client, token));
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    Stream stream = client.GetStream();
                    if (Certificate != null)
                    {
                        SslStream ssl = new SslStream(stream, false);
                        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Certificate }, token).ConfigureAwait(false);
                        stream = ssl;
                    }
                    using (stream)
                    {
                        HttpStubRequest? request = await ReadRequestAsync(stream, token).ConfigureAwait(false);
                        if (request == null) return;
                        Requests.Enqueue(request);
                        if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, token).ConfigureAwait(false);
                        HttpStubResponse response = request.Path == TokenPath ? Token(request) : Handler(request);
                        await WriteAsync(stream, request, response, token).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private HttpStubResponse Token(HttpStubRequest request)
        {
            string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(ClientId) + ":" + Uri.EscapeDataString(ClientSecret)));
            if (request.Header("Authorization") != expected) return HttpStubResponse.Json(401, "{\"error\":\"invalid_client\",\"error_description\":\"Client authentication failed.\"}");
            if (!request.Body.Contains("grant_type=client_credentials", StringComparison.Ordinal)) return HttpStubResponse.Json(400, "{\"error\":\"unsupported_grant_type\"}");
            HttpStubRequest form = new HttpStubRequest { Query = request.Body };
            LastTokenScope = form.QueryValue("scope");
            int n = Interlocked.Increment(ref _TokensIssued);
            string issued = "stub-access-token-" + n.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
            _Tokens[issued] = true;
            return HttpStubResponse.Json(200, "{\"access_token\":\"" + issued + "\",\"token_type\":\"bearer\",\"expires_in\":" + TokenLifetimeSeconds.ToString(CultureInfo.InvariantCulture) + "}");
        }

        private static async Task<HttpStubRequest?> ReadRequestAsync(Stream stream, CancellationToken token)
        {
            List<byte> head = new List<byte>();
            byte[] one = new byte[1];
            while (head.Count < 65536)
            {
                int read = await stream.ReadAsync(one, 0, 1, token).ConfigureAwait(false);
                if (read <= 0) return null;
                head.Add(one[0]);
                int c = head.Count;
                if (c >= 4 && head[c - 4] == '\r' && head[c - 3] == '\n' && head[c - 2] == '\r' && head[c - 1] == '\n') break;
            }

            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n");
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            HttpStubRequest request = new HttpStubRequest { Method = first[0] };
            string target = first[1];
            int q = target.IndexOf('?');
            request.Path = q < 0 ? target : target.Substring(0, q);
            request.Query = q < 0 ? String.Empty : target.Substring(q + 1);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                request.Headers[name] = request.Headers.TryGetValue(name, out string? existing) ? existing + ", " + value : value;
            }

            if (request.Headers.TryGetValue("Content-Length", out string? lengthText) && Int32.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int length) && length > 0)
            {
                byte[] body = new byte[length];
                int offset = 0;
                while (offset < length)
                {
                    int read = await stream.ReadAsync(body, offset, length - offset, token).ConfigureAwait(false);
                    if (read <= 0) break;
                    offset += read;
                }
                request.Body = Encoding.UTF8.GetString(body, 0, offset);
            }
            return request;
        }

        private static async Task WriteAsync(Stream stream, HttpStubRequest request, HttpStubResponse response, CancellationToken token)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(response.StatusCode.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Reason(response.StatusCode)).Append("\r\n");
            foreach (KeyValuePair<string, string> h in response.Headers) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
            sb.Append("Content-Length: ").Append(response.Body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] headBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(headBytes, 0, headBytes.Length, token).ConfigureAwait(false);
            if (request.Method != "HEAD" && response.Body.Length > 0) await stream.WriteAsync(response.Body, 0, response.Body.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 201: return "Created";
                case 204: return "No Content";
                case 301: return "Moved Permanently";
                case 302: return "Found";
                case 303: return "See Other";
                case 307: return "Temporary Redirect";
                case 308: return "Permanent Redirect";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "Status";
            }
        }

        #endregion
    }
}
