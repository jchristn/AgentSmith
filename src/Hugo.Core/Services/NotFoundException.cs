namespace Hugo.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a referenced record does not exist or is not visible; mapped to 404.
    /// </summary>
    public class NotFoundException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        public NotFoundException(string message) : base(message)
        {
        }
    }
}
