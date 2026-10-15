namespace Hugo.Server.Protocols
{
    using System;

    /// <summary>
    /// An A2A request refused with a specific JSON-RPC error code: -32001 for a task or context the caller cannot see,
    /// -32002 for a task that cannot be cancelled, -32007 for refusals (copied from Garrison).
    /// </summary>
    public class A2AAccessException : Exception
    {
        /// <summary>
        /// JSON-RPC error code.
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// Stable Hugo code for the error data, or null.
        /// </summary>
        public string? DataCode { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">JSON-RPC error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="dataCode">Stable Hugo code, or null.</param>
        public A2AAccessException(int code, string message, string? dataCode = null) : base(message)
        {
            Code = code;
            DataCode = dataCode;
        }
    }
}
