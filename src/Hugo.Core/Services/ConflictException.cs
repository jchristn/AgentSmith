namespace Hugo.Core.Services
{
    using System;

    /// <summary>
    /// Raised when an operation conflicts with existing state (duplicate name, stale lease, protected record); mapped to 409.
    /// </summary>
    public class ConflictException : Exception
    {
        /// <summary>
        /// Stable error code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message.</param>
        public ConflictException(string code, string message) : base(message)
        {
            Code = code ?? "Conflict";
        }
    }
}
