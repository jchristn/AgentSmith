namespace Hugo.Sdk
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Thrown when the Hugo server answers with a non-success status. Carries the HTTP status, the error class, the
    /// stable error code, the message, and the request identifier.
    /// </summary>
    public class HugoException : Exception
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Error class derived from the status, for example NotFound or Forbidden. Null when the body was not JSON.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Stable error code, for example RouteNotFound, ValidationFailed, or ReauthenticationRequired. Null
        /// when the body was not JSON.
        /// </summary>
        public string? Code { get; }

        /// <summary>
        /// Request identifier from the error body, or from the x-request-id response header.
        /// </summary>
        public string? RequestId { get; }

        /// <summary>
        /// Parsed error body, or null when the body was not a JSON error.
        /// </summary>
        public ErrorResponse? Response { get; }

        /// <summary>
        /// Raw response body.
        /// </summary>
        public string? ResponseBody { get; }

        /// <summary>
        /// Field-level validation messages, keyed by field name. Empty when none were returned.
        /// </summary>
        public Dictionary<string, string> FieldErrors
        {
            get
            {
                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
                if (Response?.Fields != null) foreach (KeyValuePair<string, string> pair in Response.Fields) result[pair.Key] = pair.Value;
                if (Response?.Errors != null) foreach (KeyValuePair<string, string> pair in Response.Errors) result[pair.Key] = pair.Value;
                return result;
            }
        }

        /// <summary>
        /// Whether the status is 404.
        /// </summary>
        public bool IsNotFound
        {
            get
            {
                return StatusCode == 404;
            }
        }

        /// <summary>
        /// Whether the server has no route for the request (404 with code RouteNotFound), which means the server
        /// version does not implement the API yet.
        /// </summary>
        public bool IsRouteNotFound
        {
            get
            {
                return StatusCode == 404 && String.Equals(Code, "RouteNotFound", StringComparison.Ordinal);
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="response">Parsed error body, or null.</param>
        /// <param name="responseBody">Raw response body, or null.</param>
        /// <param name="headerRequestId">Value of the x-request-id response header, or null.</param>
        public HugoException(int statusCode, ErrorResponse? response, string? responseBody, string? headerRequestId)
            : base(BuildMessage(statusCode, response, responseBody))
        {
            StatusCode = statusCode;
            Response = response;
            ResponseBody = responseBody;
            Error = response?.Error;
            Code = response?.Code;
            RequestId = !String.IsNullOrEmpty(response?.RequestId) ? response!.RequestId : headerRequestId;
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(int statusCode, ErrorResponse? response, string? responseBody)
        {
            if (response != null && !String.IsNullOrEmpty(response.Message))
            {
                return "Hugo returned " + statusCode + " " + (response.Code ?? response.Error ?? "Error") + ": " + response.Message;
            }
            string body = responseBody ?? String.Empty;
            if (body.Length > 512) body = body.Substring(0, 512);
            return "Hugo returned " + statusCode + (body.Length > 0 ? ": " + body : ".");
        }

        #endregion
    }
}
