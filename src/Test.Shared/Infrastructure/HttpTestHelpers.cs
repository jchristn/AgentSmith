namespace Test.Shared.Infrastructure
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Serialization;

    /// <summary>
    /// JSON request helpers for integration tests.
    /// </summary>
    public static class HttpTestHelpers
    {
        /// <summary>
        /// Send a request and return status and body.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="method">Method.</param>
        /// <param name="path">Path.</param>
        /// <param name="body">Optional body serialized as JSON.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        public static async Task<TestResponse> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (body != null) request.Content = new StringContent(body is string s ? s : Serializer.Serialize(body), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false))
                {
                    TestResponse result = new TestResponse();
                    result.StatusCode = (int)response.StatusCode;
                    result.Body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    return result;
                }
            }
        }

        /// <summary>
        /// Send and deserialize, asserting the expected status.
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="client">Client.</param>
        /// <param name="method">Method.</param>
        /// <param name="path">Path.</param>
        /// <param name="body">Optional body.</param>
        /// <param name="expectedStatus">Expected status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deserialized body.</returns>
        /// <exception cref="TestAssertionException">Thrown when the status differs or the body is empty.</exception>
        public static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object? body, int expectedStatus, CancellationToken token = default)
        {
            TestResponse response = await SendAsync(client, method, path, body, token).ConfigureAwait(false);
            if (response.StatusCode != expectedStatus) throw new TestAssertionException(method + " " + path + " returned " + response.StatusCode + " (expected " + expectedStatus + "): " + response.Body);
            T? value = Serializer.Deserialize<T>(response.Body);
            if (value == null) throw new TestAssertionException(method + " " + path + " returned an empty body.");
            return value;
        }
    }
}
