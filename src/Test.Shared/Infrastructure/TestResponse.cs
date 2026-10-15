namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// HTTP response captured by a test.
    /// </summary>
    public class TestResponse
    {
        /// <summary>
        /// Status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Body text.
        /// </summary>
        public string Body { get; set; } = String.Empty;
    }
}
