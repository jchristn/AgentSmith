namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using Amazon.Runtime;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Maps AWS SDK exceptions to stable <see cref="ConnectorErrorCodes"/>: access denied becomes
    /// <c>permission_denied</c>, bad or expired credentials <c>auth_failed</c>, throttling <c>rate_limited</c> with a
    /// retry-after hint, missing resources <c>not_found</c>, invalid parameters <c>invalid_argument</c>, network failures
    /// <c>connection_failed</c>, and everything else <c>upstream_error</c>. Messages never include credentials.
    /// Thread-safe.
    /// </summary>
    public static class AwsErrorMapper
    {
        #region Public-Members

        /// <summary>
        /// Retry-after hint, in seconds, attached to throttling errors (AWS does not send a Retry-After header). Default 5,
        /// minimum 1, maximum 300.
        /// </summary>
        public static double ThrottleRetryAfterSeconds
        {
            get
            {
                return _ThrottleRetryAfterSeconds;
            }
            set
            {
                _ThrottleRetryAfterSeconds = Math.Clamp(value, 1, 300);
            }
        }

        #endregion

        #region Private-Members

        private static double _ThrottleRetryAfterSeconds = 5;

        private static readonly HashSet<string> _PermissionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AccessDenied", "AccessDeniedException", "UnauthorizedOperation", "UnauthorizedAccess", "AuthorizationError",
            "AuthorizationErrorException", "Forbidden", "ForbiddenException", "NotAuthorized", "OptInRequired",
            "InsufficientPrivilegesException"
        };

        private static readonly HashSet<string> _AuthCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "InvalidClientTokenId", "UnrecognizedClientException", "SignatureDoesNotMatch", "AuthFailure", "ExpiredToken",
            "ExpiredTokenException", "IncompleteSignature", "InvalidAccessKeyId", "MissingAuthenticationToken",
            "InvalidSignatureException", "RequestExpired", "AuthFailureException"
        };

        private static readonly HashSet<string> _ThrottleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Throttling", "ThrottlingException", "ThrottledException", "RequestLimitExceeded", "TooManyRequestsException",
            "ProvisionedThroughputExceededException", "RequestThrottled", "RequestThrottledException", "SlowDown",
            "LimitExceededException", "PriorRequestNotComplete", "BandwidthLimitExceeded", "EC2ThrottledException",
            "TransactionInProgressException"
        };

        private static readonly HashSet<string> _NotFoundCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ResourceNotFoundException", "NotFoundException", "NoSuchEntity", "NoSuchHostedZone", "NoSuchKey",
            "AWS.SimpleQueueService.NonExistentQueue", "QueueDoesNotExist", "NotFound", "DBInstanceNotFound",
            "DBInstanceNotFoundFault", "DBClusterNotFoundFault", "TargetGroupNotFound", "LoadBalancerNotFound",
            "ClusterNotFoundException", "ServiceNotFoundException", "ServiceNotActiveException", "NoSuchHealthCheck"
        };

        private static readonly HashSet<string> _InvalidCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ValidationError", "ValidationException", "InvalidParameter", "InvalidParameterValue", "InvalidParameterException",
            "InvalidParameterCombination", "InvalidParameterValueException", "MissingParameter", "InvalidInput",
            "MalformedQueryException", "InvalidRequest", "InvalidRequestException", "InvalidQueryParameter",
            "InvalidAction", "InvalidArgument", "InvalidFilter", "InvalidNextToken", "InvalidPaginationToken",
            "InvalidParameterCombinationException", "InvalidInstanceID.Malformed", "InvalidInstanceState",
            "IncorrectInstanceState", "IncorrectState", "InvalidRequestContentException", "RequestTooLargeException",
            "InvalidDBInstanceState", "InvalidDBInstanceStateFault", "UnsupportedOperation", "InvalidClientTokenIdFormat",
            "InvalidAttributeName", "InvalidAddress", "SerializationException"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Convert an exception into a <see cref="ConnectorException"/> with a stable code.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <param name="operation">What was being done, for the message, for example <c>ec2:DescribeInstances</c>.</param>
        /// <returns>Connector exception (the input when it already is one).</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static ConnectorException Map(Exception e, string operation)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e is ConnectorException ce) return ce;
            if (e is AggregateException ae && ae.InnerExceptions.Count == 1) return Map(ae.InnerExceptions[0], operation);

            string what = String.IsNullOrEmpty(operation) ? "The AWS call" : operation;
            AmazonServiceException? service = Find<AmazonServiceException>(e);
            if (service != null) return MapService(service, what, e);

            if (IsConnectionFailure(e))
                return new ConnectorException(ConnectorErrorCodes.ConnectionFailed, what + " could not reach AWS: " + FirstLine(Innermost(e).Message), e);

            AmazonClientException? client = Find<AmazonClientException>(e);
            if (client != null)
            {
                string message = client.Message ?? String.Empty;
                if (message.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new ConnectorException(ConnectorErrorCodes.AuthFailed, what + " failed: AWS credentials could not be resolved (" + FirstLine(message) + ").", e);
                return new ConnectorException(ConnectorErrorCodes.UpstreamError, what + " failed in the AWS SDK: " + FirstLine(message), e);
            }

            if (e is ArgumentException || e is FormatException)
                return new ConnectorException(ConnectorErrorCodes.InvalidArgument, what + " was given an invalid value: " + FirstLine(e.Message), e);

            return new ConnectorException(ConnectorErrorCodes.UpstreamError, what + " failed: " + e.GetType().Name + ": " + FirstLine(e.Message), e);
        }

        /// <summary>
        /// Error code an AWS error code and HTTP status map to.
        /// </summary>
        /// <param name="errorCode">AWS error code, or null.</param>
        /// <param name="status">HTTP status, or 0.</param>
        /// <returns>Connector error code.</returns>
        public static string CodeFor(string? errorCode, int status)
        {
            string code = errorCode ?? String.Empty;
            if (_ThrottleCodes.Contains(code) || status == 429) return ConnectorErrorCodes.RateLimited;
            if (_AuthCodes.Contains(code)) return ConnectorErrorCodes.AuthFailed;
            if (_PermissionCodes.Contains(code) || status == 403) return ConnectorErrorCodes.PermissionDenied;
            if (_NotFoundCodes.Contains(code)
                || code.EndsWith(".NotFound", StringComparison.OrdinalIgnoreCase)
                || code.EndsWith("NotFound", StringComparison.OrdinalIgnoreCase)
                || code.EndsWith("NotFoundFault", StringComparison.OrdinalIgnoreCase)
                || code.EndsWith("NotFoundException", StringComparison.OrdinalIgnoreCase)
                || status == 404) return ConnectorErrorCodes.NotFound;
            if (_InvalidCodes.Contains(code)
                || code.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase)
                || code.EndsWith(".Malformed", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("Malformed", StringComparison.OrdinalIgnoreCase)) return ConnectorErrorCodes.InvalidArgument;
            if (status == 401) return ConnectorErrorCodes.AuthFailed;
            if (status == 503) return ConnectorErrorCodes.RateLimited;
            if (status == 400) return ConnectorErrorCodes.InvalidArgument;
            return ConnectorErrorCodes.UpstreamError;
        }

        #endregion

        #region Private-Methods

        private static ConnectorException MapService(AmazonServiceException s, string what, Exception original)
        {
            int status = (int)s.StatusCode;
            string awsCode = s.ErrorCode ?? String.Empty;
            string code = CodeFor(awsCode, status);
            string detail = FirstLine(s.Message);
            string tag = String.IsNullOrEmpty(awsCode) ? "HTTP " + status : awsCode;
            ConnectorException mapped;
            switch (code)
            {
                case ConnectorErrorCodes.RateLimited:
                    mapped = new ConnectorException(code, what + " was throttled by AWS (" + tag + "). Wait about " + _ThrottleRetryAfterSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " seconds and retry with a narrower request.", original);
                    mapped.RetryAfterSeconds = _ThrottleRetryAfterSeconds;
                    return mapped;
                case ConnectorErrorCodes.AuthFailed:
                    return new ConnectorException(code, what + " failed authentication (" + tag + "): " + detail + " Check the access key pair or role configuration.", original);
                case ConnectorErrorCodes.PermissionDenied:
                    return new ConnectorException(code, what + " was denied by IAM (" + tag + "): " + detail + " The resource's credentials lack this permission; see docs/connectors/aws-iam-policies.md.", original);
                case ConnectorErrorCodes.NotFound:
                    return new ConnectorException(code, what + " found nothing (" + tag + "): " + detail, original);
                case ConnectorErrorCodes.InvalidArgument:
                    return new ConnectorException(code, what + " rejected the request (" + tag + "): " + detail, original);
                default:
                    return new ConnectorException(code, what + " failed (" + tag + (status > 0 ? ", HTTP " + status : String.Empty) + "): " + detail, original);
            }
        }

        private static T? Find<T>(Exception e) where T : Exception
        {
            Exception? current = e;
            while (current != null)
            {
                if (current is T typed) return typed;
                current = current.InnerException;
            }
            return null;
        }

        private static bool IsConnectionFailure(Exception e)
        {
            Exception? current = e;
            while (current != null)
            {
                if (current is SocketException || current is HttpRequestException || current is WebException || current is IOException) return true;
                current = current.InnerException;
            }
            return false;
        }

        private static Exception Innermost(Exception e)
        {
            Exception current = e;
            while (current.InnerException != null) current = current.InnerException;
            return current;
        }

        private static string FirstLine(string? message)
        {
            if (String.IsNullOrEmpty(message)) return String.Empty;
            int newline = message.IndexOfAny(new char[] { '\r', '\n' });
            string line = newline >= 0 ? message.Substring(0, newline) : message;
            line = line.Trim();
            return line.Length > 400 ? line.Substring(0, 400) + "..." : line;
        }

        #endregion
    }
}
