namespace Hugo.Core.Services
{
    using System;

    /// <summary>Safe, structured document delivery or limit failure.</summary>
    public class PromptDocumentException : Exception
    {
        /// <summary>HTTP status, normally 413 or 422.</summary>
        public int StatusCode { get; }
        /// <summary>Stable bounded error code.</summary>
        public string Code { get; }
        /// <summary>Instantiate without storing document content.</summary>
        /// <param name="code">Stable code.</param>
        /// <param name="message">Safe explanation.</param>
        /// <param name="statusCode">HTTP status.</param>
        public PromptDocumentException(string code, string message, int statusCode = 422) : base(message) { Code = code; StatusCode = statusCode; }
    }
}
