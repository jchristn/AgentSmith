namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured <c>data</c> of an A2A JSON-RPC error: a stable Hugo code plus details (copied from Garrison).
    /// </summary>
    public class A2AErrorData
    {
        /// <summary>
        /// Stable error code, for example ValidationFailed, TurnInProgress, or ModelEndpointRequired.
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// Field errors, by field.
        /// </summary>
        public Dictionary<string, string>? Errors { get; set; } = null;

        /// <summary>
        /// Task the error concerns, when any.
        /// </summary>
        public string? TaskId { get; set; } = null;

        /// <summary>
        /// Approval the error concerns, when any.
        /// </summary>
        public string? ApprovalId { get; set; } = null;
    }
}
