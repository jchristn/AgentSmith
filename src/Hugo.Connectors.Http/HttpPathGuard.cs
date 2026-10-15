namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Pure checks that keep a request path inside the resource. A path is always relative to the base URL path. It
    /// must not be an absolute or protocol-relative URL, must not contain backslashes, control characters, <c>?</c>, or
    /// <c>#</c> (query parameters go in the <c>query</c> argument), and after repeated percent-decoding must contain no
    /// <c>.</c> or <c>..</c> segments, no empty segments, and no encoded slashes. The decoded path must then fall under
    /// one of the allowed prefixes on a segment boundary (<c>/api</c> allows <c>/api</c> and <c>/api/x</c>, not
    /// <c>/apix</c>). Thread-safe.
    /// </summary>
    public static class HttpPathGuard
    {
        #region Private-Members

        private static readonly Regex _Scheme = new Regex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant);
        private static readonly int _MaxDecodeRounds = 4;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check a path argument against the allowed prefixes.
        /// </summary>
        /// <param name="path">Path argument. Null or empty means <c>/</c>.</param>
        /// <param name="allowedPrefixes">Normalized allowed prefixes. Null or empty allows every path.</param>
        /// <returns>Result with the normalized and encoded path, or the refusal reason.</returns>
        public static HttpPathCheck Check(string? path, IList<string>? allowedPrefixes)
        {
            string raw = String.IsNullOrEmpty(path) ? "/" : path;
            string? malformed = Malformed(raw);
            if (malformed != null) return HttpPathCheck.Refuse(ConnectorErrorCodes.InvalidArgument, malformed);
            if (!raw.StartsWith('/')) raw = "/" + raw;

            string decoded = raw;
            for (int round = 0; ; round++)
            {
                if (decoded.IndexOf("%2f", StringComparison.OrdinalIgnoreCase) >= 0 || decoded.IndexOf("%5c", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HttpPathCheck.Refuse(ConnectorErrorCodes.InvalidArgument, "Path '" + Shorten(path) + "' contains an encoded slash or backslash. Use plain '/' separators.");
                string next = Uri.UnescapeDataString(decoded);
                if (next == decoded) break;
                if (round >= _MaxDecodeRounds)
                    return HttpPathCheck.Refuse(ConnectorErrorCodes.InvalidArgument, "Path '" + Shorten(path) + "' is percent-encoded too many times.");
                decoded = next;
                string? hidden = Malformed(decoded);
                if (hidden != null) return HttpPathCheck.Refuse(ConnectorErrorCodes.InvalidArgument, "Path '" + Shorten(path) + "' hides a forbidden character in percent-encoding. " + hidden);
            }

            string[] segments = decoded.Split('/');
            for (int i = 1; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 && i < segments.Length - 1)
                    return HttpPathCheck.Refuse(ConnectorErrorCodes.InvalidArgument, "Path '" + Shorten(path) + "' contains an empty segment (//).");
                if (segment == "." || segment == "..")
                    return HttpPathCheck.Refuse(ConnectorErrorCodes.InvalidArgument, "Path '" + Shorten(path) + "' contains a '" + segment + "' segment. Paths may not traverse directories.");
            }

            if (allowedPrefixes != null && allowedPrefixes.Any() && !allowedPrefixes.Any(p => MatchesPrefix(decoded, p)))
                return HttpPathCheck.Refuse(ConnectorErrorCodes.PermissionDenied, "Path '" + Shorten(decoded) + "' is outside the allowed path prefixes of this resource (" + String.Join(", ", allowedPrefixes) + ").");

            StringBuilder encoded = new StringBuilder();
            for (int i = 1; i < segments.Length; i++)
            {
                encoded.Append('/').Append(EscapeSegment(segments[i]));
            }
            if (encoded.Length == 0) encoded.Append('/');
            return HttpPathCheck.Ok(decoded, encoded.ToString());
        }

        /// <summary>
        /// Whether a decoded path falls under a normalized prefix on a segment boundary.
        /// </summary>
        /// <param name="path">Decoded path starting with <c>/</c>.</param>
        /// <param name="prefix">Normalized prefix (see <see cref="NormalizePrefix"/>).</param>
        /// <returns>True when the path is the prefix or below it.</returns>
        public static bool MatchesPrefix(string path, string prefix)
        {
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(prefix)) return false;
            if (prefix == "/") return true;
            if (String.Equals(path, prefix, StringComparison.Ordinal)) return true;
            if (String.Equals(path, prefix + "/", StringComparison.Ordinal)) return true;
            return path.StartsWith(prefix + "/", StringComparison.Ordinal);
        }

        /// <summary>
        /// Normalize a prefix: leading <c>/</c>, no trailing <c>/</c> except for the root.
        /// </summary>
        /// <param name="prefix">Prefix.</param>
        /// <returns>Normalized prefix.</returns>
        public static string NormalizePrefix(string? prefix)
        {
            string p = (prefix ?? String.Empty).Trim();
            if (!p.StartsWith('/')) p = "/" + p;
            while (p.Length > 1 && p.EndsWith('/')) p = p.Substring(0, p.Length - 1);
            return p;
        }

        /// <summary>
        /// Whether a configured prefix is acceptable: starts with <c>/</c>, no dot segments, no query, fragment,
        /// backslash, percent sign, or control characters.
        /// </summary>
        /// <param name="prefix">Prefix.</param>
        /// <param name="error">Reason when invalid.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidPrefix(string? prefix, out string error)
        {
            error = String.Empty;
            string p = (prefix ?? String.Empty).Trim();
            if (!p.StartsWith('/'))
            {
                error = "Path prefix '" + p + "' must start with '/'.";
                return false;
            }
            if (p.IndexOfAny(new char[] { '?', '#', '\\', '%', '*' }) >= 0 || p.Any(Char.IsControl) || p.StartsWith("//", StringComparison.Ordinal))
            {
                error = "Path prefix '" + p + "' may not contain ?, #, \\, %, *, control characters, or start with //.";
                return false;
            }
            if (p.Split('/').Any(s => s == "." || s == ".."))
            {
                error = "Path prefix '" + p + "' may not contain '.' or '..' segments.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Normalize a base URL path: no trailing slash; the root becomes the empty string.
        /// </summary>
        /// <param name="absolutePath">Encoded absolute path of the base URL.</param>
        /// <returns>Base path, for example <c>/api/v1</c> or empty.</returns>
        public static string NormalizeBasePath(string? absolutePath)
        {
            string p = absolutePath ?? String.Empty;
            while (p.EndsWith('/')) p = p.Substring(0, p.Length - 1);
            return p;
        }

        /// <summary>
        /// Express an encoded absolute path (for example a redirect target) relative to the base path.
        /// </summary>
        /// <param name="encodedAbsolutePath">Encoded absolute path.</param>
        /// <param name="basePath">Normalized base path (see <see cref="NormalizeBasePath"/>).</param>
        /// <param name="relative">Encoded path relative to the base path, starting with <c>/</c>.</param>
        /// <returns>True when the path is the base path or below it.</returns>
        public static bool TryRelativeToBase(string encodedAbsolutePath, string basePath, out string relative)
        {
            relative = String.Empty;
            string p = String.IsNullOrEmpty(encodedAbsolutePath) ? "/" : encodedAbsolutePath;
            if (String.IsNullOrEmpty(basePath))
            {
                relative = p;
                return true;
            }
            if (String.Equals(p, basePath, StringComparison.Ordinal))
            {
                relative = "/";
                return true;
            }
            if (!p.StartsWith(basePath + "/", StringComparison.Ordinal)) return false;
            relative = p.Substring(basePath.Length);
            return true;
        }

        /// <summary>
        /// Percent-encode one path segment, keeping RFC 3986 unreserved characters, sub-delimiters, <c>:</c>, and
        /// <c>@</c>.
        /// </summary>
        /// <param name="segment">Decoded segment.</param>
        /// <returns>Encoded segment.</returns>
        public static string EscapeSegment(string segment)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(segment ?? String.Empty))
            {
                char c = (char)b;
                bool keep = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "-._~!$&'()*+,;=:@".IndexOf(c) >= 0;
                if (b < 0x80 && keep) sb.Append(c);
                else sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static string? Malformed(string value)
        {
            if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("\\", StringComparison.Ordinal))
                return "Protocol-relative URLs are not allowed; give a path relative to the resource's base URL.";
            if (_Scheme.IsMatch(value) || value.Contains("://", StringComparison.Ordinal))
                return "Absolute URLs are not allowed; give a path relative to the resource's base URL.";
            if (value.IndexOf('\\') >= 0) return "Backslashes are not allowed in paths.";
            if (value.Any(c => Char.IsControl(c))) return "Control characters are not allowed in paths.";
            if (value.IndexOf('?') >= 0) return "Put query parameters in the 'query' argument, not in the path.";
            if (value.IndexOf('#') >= 0) return "Fragments (#) are not allowed in paths.";
            return null;
        }

        private static string Shorten(string? value)
        {
            string v = (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ');
            return v.Length > 200 ? v.Substring(0, 197) + "..." : v;
        }

        #endregion
    }
}
