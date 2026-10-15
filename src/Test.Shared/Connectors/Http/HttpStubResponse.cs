namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// A response returned by a <see cref="HttpStubServer"/> handler.
    /// </summary>
    public class HttpStubResponse
    {
        /// <summary>
        /// Status code. Default 200.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Headers to send (Content-Length and Connection are added by the server).
        /// </summary>
        public List<KeyValuePair<string, string>> Headers { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Body bytes.
        /// </summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// A text response.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="body">Body.</param>
        /// <param name="contentType">Content type.</param>
        /// <returns>Response.</returns>
        public static HttpStubResponse Text(int status, string body, string contentType = "text/plain; charset=utf-8")
        {
            HttpStubResponse r = new HttpStubResponse { StatusCode = status, Body = Encoding.UTF8.GetBytes(body ?? String.Empty) };
            r.Headers.Add(new KeyValuePair<string, string>("Content-Type", contentType));
            return r;
        }

        /// <summary>
        /// A JSON response.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="json">JSON text.</param>
        /// <returns>Response.</returns>
        public static HttpStubResponse Json(int status, string json)
        {
            return Text(status, json, "application/json");
        }

        /// <summary>
        /// A redirect.
        /// </summary>
        /// <param name="status">Status (301, 302, 303, 307, 308).</param>
        /// <param name="location">Location header.</param>
        /// <returns>Response.</returns>
        public static HttpStubResponse Redirect(int status, string location)
        {
            HttpStubResponse r = new HttpStubResponse { StatusCode = status };
            r.Headers.Add(new KeyValuePair<string, string>("Location", location));
            return r;
        }

        /// <summary>
        /// Add a header and return this response.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="value">Value.</param>
        /// <returns>This response.</returns>
        public HttpStubResponse With(string name, string value)
        {
            Headers.Add(new KeyValuePair<string, string>(name, value));
            return this;
        }
    }
}
