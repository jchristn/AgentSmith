namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Error body returned by every API. Code is a stable identifier the dashboard localizes.
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// HTTP-level error name, for example Forbidden.
        /// </summary>
        public string Error { get; set; } = String.Empty;

        /// <summary>
        /// Stable error code, for example AuthorizationDenied.
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Request identifier.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Required permission, for authorization failures.
        /// </summary>
        public string? RequiredPermission { get; set; } = null;

        /// <summary>
        /// Resource type, for authorization failures.
        /// </summary>
        public string? ResourceType { get; set; } = null;

        /// <summary>
        /// Tenant identifier, when known.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Field-level details for validation failures.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string>? Fields { get; set; } = null;

        /// <summary>
        /// Validation errors keyed by field or setting path (for example Audit.RetentionDays), with a human message for
        /// each. Same content as Fields; present on every 400 ValidationFailed response that names fields.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string>? Errors { get; set; } = null;

        /// <summary>
        /// Stable machine code for each entry in Errors, keyed by the same path (for example OutOfRange,
        /// NotAnInteger, ParameterNotAllowed, or Invalid when no specific code applies), for translated messages.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string>? FieldCodes { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ErrorResponse()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="error">Error name.</param>
        /// <param name="code">Stable code.</param>
        /// <param name="message">Message.</param>
        public ErrorResponse(string error, string code, string message)
        {
            Error = error ?? String.Empty;
            Code = code ?? String.Empty;
            Message = message ?? String.Empty;
        }
    }
}
