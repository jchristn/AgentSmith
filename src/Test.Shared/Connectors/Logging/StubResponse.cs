namespace Test.Shared.Connectors.Logging
{
    /// <summary>
    /// A stub HTTP response: status code and an optional body serialized as JSON.
    /// </summary>
    public class StubResponse
    {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Body serialized as JSON, or null for a plain-text body.
        /// </summary>
        public object? Body { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="statusCode">Status code.</param>
        /// <param name="body">Body, or null.</param>
        public StubResponse(int statusCode, object? body)
        {
            StatusCode = statusCode;
            Body = body;
        }
    }
}
