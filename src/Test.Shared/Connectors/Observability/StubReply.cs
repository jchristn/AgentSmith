namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A reply produced by a stub route.
    /// </summary>
    public class StubReply
    {
        /// <summary>
        /// Status code.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Body text.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        /// <summary>
        /// Extra response headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A 200 JSON reply.
        /// </summary>
        /// <param name="json">Body.</param>
        /// <returns>Reply.</returns>
        public static StubReply Json(string json)
        {
            return new StubReply { Body = json };
        }

        /// <summary>
        /// A JSON reply with a status.
        /// </summary>
        /// <param name="status">Status code.</param>
        /// <param name="json">Body.</param>
        /// <returns>Reply.</returns>
        public static StubReply Status(int status, string json)
        {
            return new StubReply { StatusCode = status, Body = json };
        }
    }
}
