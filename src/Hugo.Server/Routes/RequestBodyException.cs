namespace Hugo.Server.Routes
{
    using System;

    /// <summary>
    /// Raised when a request body is missing or malformed; mapped to 400.
    /// </summary>
    public class RequestBodyException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        public RequestBodyException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception.</param>
        public RequestBodyException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
