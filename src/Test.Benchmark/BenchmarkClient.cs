namespace Hugo.Benchmark
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Single pooled public REST client, without product assembly references. Not thread-safe.</summary>
    public class BenchmarkClient : IDisposable
    {
        private readonly HttpClient _Http;
        private bool _Disposed;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

        /// <summary>Create an authenticated connection pool.</summary>
        /// <param name="url">Explicit isolated SUT base URL.</param><param name="token">Environment-provided bearer token.</param>
        public BenchmarkClient(string url, string token)
        {
            Uri origin = new Uri(url);
            if (origin.Scheme != "http" && origin.Scheme != "https") throw new ArgumentException("Use an HTTP or HTTPS SUT URL.");
            SocketsHttpHandler handler = new SocketsHttpHandler { MaxConnectionsPerServer = 4, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            _Http = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(60) };
            _Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>Send a typed request; errors never print returned bodies or credentials.</summary>
        /// <param name="method">Method.</param><param name="path">Public API path.</param><param name="body">Typed/anonymous fixed request.</param><param name="token">Cancellation.</param>
        /// <returns>Typed API metadata.</returns>
        public async Task<ApiRecord> CallAsync(HttpMethod method, string path, object? body, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body, _Json), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("Benchmark API request failed: " + method + " " + path.Split('?')[0] + " HTTP " + (int)response.StatusCode, null, response.StatusCode);
                    if (response.StatusCode == HttpStatusCode.NoContent) return new ApiRecord();
                    string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<ApiRecord>(text, _Json) ?? throw new InvalidOperationException("Missing typed API response.");
                }
            }
        }

        /// <summary>Dispose the owned HTTP pool.</summary>
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        /// <summary>Release resources once.</summary><param name="disposing">Release managed resources.</param>
        protected virtual void Dispose(bool disposing) { if (_Disposed) return; if (disposing) _Http.Dispose(); _Disposed = true; }
    }
}
