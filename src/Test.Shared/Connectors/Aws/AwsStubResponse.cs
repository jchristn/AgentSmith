namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One canned HTTP response of the AWS stub: status, content type, body, and extra headers.
    /// </summary>
    public class AwsStubResponse
    {
        #region Public-Members

        /// <summary>
        /// HTTP status. Default 200.
        /// </summary>
        public int Status { get; set; } = 200;

        /// <summary>
        /// Content type. Default <c>text/xml</c>.
        /// </summary>
        public string ContentType { get; set; } = "text/xml";

        /// <summary>
        /// Body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Extra response headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// An awsQuery (XML) success response.
        /// </summary>
        /// <param name="body">XML body.</param>
        /// <returns>Response.</returns>
        public static AwsStubResponse Xml(string body)
        {
            return new AwsStubResponse { Status = 200, ContentType = "text/xml", Body = body };
        }

        /// <summary>
        /// An awsJson 1.1 success response.
        /// </summary>
        /// <param name="body">JSON body.</param>
        /// <returns>Response.</returns>
        public static AwsStubResponse Json(string body)
        {
            return new AwsStubResponse { Status = 200, ContentType = "application/x-amz-json-1.1", Body = body };
        }

        /// <summary>
        /// An awsQuery error (the ErrorResponse shape used by STS, RDS, ELBv2, and Auto Scaling).
        /// </summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="code">AWS error code.</param>
        /// <param name="message">Message.</param>
        /// <returns>Response.</returns>
        public static AwsStubResponse QueryError(int status, string code, string message)
        {
            string body = "<ErrorResponse xmlns=\"http://rds.amazonaws.com/doc/2014-10-31/\"><Error><Type>Sender</Type><Code>" + code + "</Code><Message>"
                + System.Security.SecurityElement.Escape(message) + "</Message></Error><RequestId>stub-error-request</RequestId></ErrorResponse>";
            return new AwsStubResponse { Status = status, ContentType = "text/xml", Body = body };
        }

        /// <summary>
        /// An EC2 error (the Response/Errors shape).
        /// </summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="code">AWS error code.</param>
        /// <param name="message">Message.</param>
        /// <returns>Response.</returns>
        public static AwsStubResponse Ec2Error(int status, string code, string message)
        {
            string body = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response><Errors><Error><Code>" + code + "</Code><Message>"
                + System.Security.SecurityElement.Escape(message) + "</Message></Error></Errors><RequestID>stub-error-request</RequestID></Response>";
            return new AwsStubResponse { Status = status, ContentType = "text/xml", Body = body };
        }

        /// <summary>
        /// An awsJson error.
        /// </summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="code">AWS error code, for example <c>AccessDeniedException</c>.</param>
        /// <param name="message">Message.</param>
        /// <returns>Response.</returns>
        public static AwsStubResponse JsonError(int status, string code, string message)
        {
            AwsStubResponse r = new AwsStubResponse
            {
                Status = status,
                ContentType = "application/x-amz-json-1.1",
                Body = "{\"__type\":\"" + code + "\",\"message\":\"" + message.Replace("\"", "'") + "\"}"
            };
            r.Headers["x-amzn-ErrorType"] = code;
            return r;
        }

        #endregion
    }
}
