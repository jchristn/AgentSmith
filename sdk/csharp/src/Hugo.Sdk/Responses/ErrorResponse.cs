namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Error body returned by the server.
    /// </summary>
    public class ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Error class derived from the HTTP status, for example NotFound.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Stable error code, for example RouteNotFound or ReauthenticationRequired.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Request Identifier.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Required permission.
        /// </summary>
        public string? RequiredPermission { get; set; } = null;

        /// <summary>
        /// Resource type.
        /// </summary>
        public string? ResourceType { get; set; } = null;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Field-level messages.
        /// </summary>
        public Dictionary<string, string>? Fields { get; set; } = null;

        /// <summary>
        /// Field-level errors.
        /// </summary>
        public Dictionary<string, string>? Errors { get; set; } = null;

        /// <summary>
        /// Field-level error codes.
        /// </summary>
        public Dictionary<string, string>? FieldCodes { get; set; } = null;

        #endregion
    }
}
