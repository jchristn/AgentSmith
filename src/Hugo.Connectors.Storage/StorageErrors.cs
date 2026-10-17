namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using Amazon.Runtime;
    using Amazon.S3;
    using Azure;
    using Google;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Maps provider SDK exceptions to stable connector error codes: <c>not_found</c>, <c>permission_denied</c>,
    /// <c>invalid_credentials</c>, <c>invalid_setting</c>, <c>conflict</c>, <c>connection_failed</c>, <c>throttled</c>, and <c>upstream_error</c>. Messages never include
    /// credentials. Thread-safe.
    /// </summary>
    public static class StorageErrors
    {
        #region Public-Methods

        /// <summary>
        /// Convert an exception into a <see cref="ConnectorException"/>.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <param name="containerName">Bucket or container, for messages.</param>
        /// <param name="key">Object key involved, or null.</param>
        /// <returns>Connector exception (the input when it already is one).</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static ConnectorException Map(Exception e, string containerName, string? key)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e is ConnectorException ce) return ce;
            if (e is AggregateException ae && ae.InnerExceptions.Count == 1) return Map(ae.InnerExceptions[0], containerName, key);

            string target = String.IsNullOrEmpty(key) ? "bucket or container '" + containerName + "'" : "object '" + key + "' in '" + containerName + "'";
            int? status = StatusOf(e);

            if (e is KeyNotFoundException || e is FileNotFoundException || status == 404)
                return new ConnectorException("not_found", "Not found: " + target + ".", e);
            AmazonS3Exception? s3 = FindS3(e);
            if (s3 != null)
            {
                ConnectorException? specific = MapS3Authentication(s3, e);
                if (specific != null) return specific;
            }
            if (status == 401 || status == 403)
                return new ConnectorException("permission_denied", "The storage service denied access to " + target + " (HTTP " + status + "). Check the credentials and their permissions.", e);
            if (status == 409 || status == 412)
                return new ConnectorException("conflict", "The storage service reported a conflict for " + target + " (HTTP " + status + ").", e);
            if (status == 429 || status == 503)
                return new ConnectorException("throttled", "The storage service is throttling or unavailable (HTTP " + status + "); retry later.", e);
            if (IsConnectionFailure(e))
                return new ConnectorException("connection_failed", "Could not reach the storage service: " + FirstLine(Innermost(e).Message), e);
            if (s3 != null)
                return new ConnectorException("upstream_error", "AWS S3 request failed" + (status.HasValue ? " (HTTP " + status + ")" : String.Empty) + " for " + target + ". Check the bucket region, endpoint, and service configuration.", e);
            if (status != null)
                return new ConnectorException("upstream_error", "The storage service returned HTTP " + status + " for " + target + ": " + FirstLine(e.Message), e);
            return new ConnectorException("upstream_error", "Storage operation on " + target + " failed: " + e.GetType().Name + ": " + FirstLine(e.Message), e);
        }

        /// <summary>
        /// Whether an exception means the object does not exist.
        /// </summary>
        /// <param name="e">Exception.</param>
        /// <returns>True for not-found errors.</returns>
        public static bool IsNotFound(Exception e)
        {
            if (e == null) return false;
            if (e is KeyNotFoundException || e is FileNotFoundException) return true;
            return StatusOf(e) == 404;
        }

        #endregion

        #region Private-Methods

        private static AmazonS3Exception? FindS3(Exception error)
        {
            Exception? current = error;
            while (current != null)
            {
                if (current is AmazonS3Exception s3) return s3;
                current = current.InnerException;
            }
            return null;
        }

        private static ConnectorException? MapS3Authentication(AmazonS3Exception error, Exception original)
        {
            // Use only fixed messages for recognized codes. Provider messages can contain request/signing material.
            switch (error.ErrorCode)
            {
                case "InvalidAccessKeyId":
                    return new ConnectorException("invalid_credentials", "AWS S3 InvalidAccessKeyId: the access key ID was not recognized. Check the bound accessKeyPair secret and whether the key is active.", original);
                case "SignatureDoesNotMatch":
                case "InvalidSignature":
                    return new ConnectorException("invalid_credentials", "AWS S3 SignatureDoesNotMatch: the request signature was rejected. Check the bound access key and secret key pair, bucket region, endpoint, and local clock.", original);
                case "ExpiredToken":
                case "InvalidToken":
                case "TokenRefreshRequired":
                case "InvalidSecurity":
                    return new ConnectorException("invalid_credentials", "AWS S3 rejected the authentication credentials or token. Check that the bound accessKeyPair secret is valid and current.", original);
                case "RequestTimeTooSkewed":
                case "RequestExpired":
                    return new ConnectorException("invalid_credentials", "AWS S3 rejected the request timestamp. Check and synchronize the Hugo host's clock before retrying.", original);
                case "AuthorizationHeaderMalformed":
                case "PermanentRedirect":
                case "IncorrectEndpoint":
                case "IllegalLocationConstraintException":
                    return new ConnectorException("invalid_setting", "AWS S3 rejected the request region or endpoint. Set region to the bucket's AWS region and leave endpoint empty for Amazon S3.", original);
                case "AccessDenied":
                case "AllAccessDisabled":
                    return new ConnectorException("permission_denied", "AWS S3 AccessDenied: the service denied this operation. Check IAM and bucket policies, explicit denies, credential validity, and any prefix conditions.", original);
                default:
                    return null;
            }
        }

        private static int? StatusOf(Exception e)
        {
            Exception? current = e;
            while (current != null)
            {
                if (current is AmazonS3Exception s3 && (int)s3.StatusCode > 0)
                {
                    if (String.Equals(s3.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(s3.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase)) return 404;
                    return (int)s3.StatusCode;
                }
                if (current is AmazonServiceException svc && (int)svc.StatusCode > 0) return (int)svc.StatusCode;
                if (current is RequestFailedException rfe && rfe.Status > 0) return rfe.Status;
                if (current is GoogleApiException gae && (int)gae.HttpStatusCode > 0) return (int)gae.HttpStatusCode;
                if (current is HttpRequestException hre && hre.StatusCode != null) return (int)hre.StatusCode.Value;
                current = current.InnerException;
            }
            return null;
        }

        private static bool IsConnectionFailure(Exception e)
        {
            Exception? current = e;
            while (current != null)
            {
                if (current is SocketException || current is HttpRequestException || current is WebException) return true;
                if (current is RequestFailedException rfe && rfe.Status == 0) return true;
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
            return line.Length > 300 ? line.Substring(0, 300) + "..." : line;
        }

        #endregion
    }
}
