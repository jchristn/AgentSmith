namespace Hugo.Core.Security
{
    using System;

    /// <summary>
    /// Raised when the secrets master key is missing, malformed, or does not match the stored canary. The server refuses
    /// to start.
    /// </summary>
    public class MasterKeyException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        public MasterKeyException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate with an inner exception.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception.</param>
        public MasterKeyException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
