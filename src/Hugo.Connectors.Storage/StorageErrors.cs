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
    /// <c>conflict</c>, <c>connection_failed</c>, <c>throttled</c>, and <c>upstream_error</c>. Messages never include
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
            if (status == 401 || status == 403)
                return new ConnectorException("permission_denied", "The storage service denied access to " + target + " (HTTP " + status + "). Check the credentials and their permissions.", e);
            if (status == 409 || status == 412)
                return new ConnectorException("conflict", "The storage service reported a conflict for " + target + " (HTTP " + status + ").", e);
            if (status == 429 || status == 503)
                return new ConnectorException("throttled", "The storage service is throttling or unavailable (HTTP " + status + "); retry later.", e);
            if (IsConnectionFailure(e))
                return new ConnectorException("connection_failed", "Could not reach the storage service: " + FirstLine(Innermost(e).Message), e);
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
