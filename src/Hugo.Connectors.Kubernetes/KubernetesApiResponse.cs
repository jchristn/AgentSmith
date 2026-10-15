namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Net;

    /// <summary>
    /// A raw API server response: status code, body text, and the Retry-After hint. Not thread-safe.
    /// </summary>
    public class KubernetesApiResponse
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        /// <summary>
        /// Response body (UTF-8). Never null.
        /// </summary>
        public string Body
        {
            get
            {
                return _Body;
            }
            set
            {
                _Body = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Seconds from a Retry-After header, or null.
        /// </summary>
        public double? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Whether the status code is 2xx.
        /// </summary>
        public bool IsSuccess
        {
            get
            {
                int code = (int)StatusCode;
                return code >= 200 && code < 300;
            }
        }

        #endregion

        #region Private-Members

        private string _Body = String.Empty;

        #endregion
    }
}
