namespace Hugo.Core.Services
{
    using System;

    /// <summary>
    /// The caller is known and the target exists, but the action is not allowed (HTTP 403), for example a credential
    /// locked to another profile.
    /// </summary>
    public class ForbiddenException : Exception
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
        public ForbiddenException(string code, string message) : base(message)
        {
            Code = String.IsNullOrWhiteSpace(code) ? "Forbidden" : code;
        }
    }
}
