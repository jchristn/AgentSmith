namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Removes secret material from text before it reaches a log, request history, an audit description, a prompt, or
    /// tool output (P2.9, plan section 12.5). Two layers: known plaintext values registered on this instance (exact,
    /// base64, and URL-encoded forms), then patterns for private key blocks, AWS access key identifiers and secret keys,
    /// bearer tokens, URI and connection string passwords, password pairs, and generic token assignments. Every
    /// redaction is replaced with [redacted:kind] and counted in hugo_secret_redactions_total. Thread-safe.
    /// </summary>
    public class SecretScrubber
    {
        #region Public-Members

        /// <summary>
        /// Shared pattern-only scrubber for logs, request history, and audit descriptions.
        /// </summary>
        public static SecretScrubber Shared
        {
            get
            {
                return _Shared;
            }
        }

        /// <summary>
        /// Minimum length of a known value to register. Shorter values are ignored because redacting them would mangle
        /// ordinary text. Minimum 1, maximum 64, default 4.
        /// </summary>
        public int MinimumKnownValueLength
        {
            get
            {
                return _MinimumKnownValueLength;
            }
            set
            {
                _MinimumKnownValueLength = Math.Clamp(value, 1, 64);
            }
        }

        /// <summary>
        /// Number of distinct known values registered.
        /// </summary>
        public int KnownValueCount
        {
            get
            {
                lock (_Lock)
                {
                    return _Known.Count;
                }
            }
        }

        #endregion

        #region Private-Members

        private static readonly SecretScrubber _Shared = new SecretScrubber();
        private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(1);
        private static readonly List<ScrubPattern> _Patterns = new List<ScrubPattern>
        {
            new ScrubPattern("private_key", new Regex(@"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----[\s\S]*?(?:-----END (?:[A-Z0-9]+ )*PRIVATE KEY-----|$)", RegexOptions.Compiled, _Timeout), null),
            new ScrubPattern("aws_access_key", new Regex(@"\b(?:AKIA|ASIA|AGPA|AIDA|AROA|ANPA|ANVA|AIPA)[0-9A-Z]{16}\b", RegexOptions.Compiled, _Timeout), null),
            new ScrubPattern("aws_secret_key", new Regex(@"(?i)(aws_secret_access_key|secretAccessKey|aws_session_token|sessionToken)(\s*[""']?\s*[:=]\s*[""']?)[A-Za-z0-9/+=]{16,}", RegexOptions.Compiled, _Timeout), "$1$2"),
            new ScrubPattern("bearer", new Regex(@"(?i)\b(bearer\s+)[A-Za-z0-9\-._~+/]{8,}=*", RegexOptions.Compiled, _Timeout), "$1"),
            new ScrubPattern("connection_string", new Regex(@"(?i)\b([a-z][a-z0-9+.\-]*://[^:/\s@]+:)[^@\s/]+(@)", RegexOptions.Compiled, _Timeout), "$1$2"),
            new ScrubPattern("password_pair", new Regex(@"(?i)\b(password|passwd|pwd|passphrase)(\s*[""']?\s*[:=]\s*[""']?)[^\s;,&""'}]+", RegexOptions.Compiled, _Timeout), "$1$2"),
            new ScrubPattern("generic", new Regex(@"(?i)\b(secret|client_secret|api[_\-]?key|access[_\-]?token|refresh[_\-]?token|auth[_\-]?token|x-secret-key|x-api-key)(\s*[""']?\s*[:=]\s*[""']?)[A-Za-z0-9/+_\-.]{8,}", RegexOptions.Compiled, _Timeout), "$1$2")
        };

        private readonly object _Lock = new object();
        private readonly HashSet<string> _Known = new HashSet<string>(StringComparer.Ordinal);
        private int _MinimumKnownValueLength = 4;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a plaintext value (and its base64 and URL-encoded forms) for exact-match redaction.
        /// </summary>
        /// <param name="value">Plaintext; null, empty, or short values are ignored.</param>
        public void AddKnownValue(string? value)
        {
            if (String.IsNullOrEmpty(value) || value.Length < _MinimumKnownValueLength) return;
            lock (_Lock)
            {
                _Known.Add(value);
                _Known.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
                string escaped = Uri.EscapeDataString(value);
                if (!String.Equals(escaped, value, StringComparison.Ordinal)) _Known.Add(escaped);
                string trimmed = value.Trim();
                if (trimmed.Length >= _MinimumKnownValueLength && !String.Equals(trimmed, value, StringComparison.Ordinal)) _Known.Add(trimmed);
            }
        }

        /// <summary>
        /// Register several plaintext values.
        /// </summary>
        /// <param name="values">Values.</param>
        public void AddKnownValues(IEnumerable<string?>? values)
        {
            if (values == null) return;
            foreach (string? value in values) AddKnownValue(value);
        }

        /// <summary>
        /// Forget every registered value.
        /// </summary>
        public void ClearKnownValues()
        {
            lock (_Lock)
            {
                _Known.Clear();
            }
        }

        /// <summary>
        /// Scrub text.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Scrubbed text (null stays null).</returns>
        public string? Scrub(string? text)
        {
            return Scrub(text, out int _);
        }

        /// <summary>
        /// Scrub text and report how many redactions were made.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="redactions">Number of redactions.</param>
        /// <returns>Scrubbed text (null stays null).</returns>
        public string? Scrub(string? text, out int redactions)
        {
            redactions = 0;
            if (String.IsNullOrEmpty(text)) return text;

            string result = text;
            List<string> known;
            lock (_Lock)
            {
                known = _Known.OrderByDescending(k => k.Length).ToList();
            }
            foreach (string value in known)
            {
                int count = Count(result, value);
                if (count == 0) continue;
                result = result.Replace(value, "[redacted:known]", StringComparison.Ordinal);
                redactions += count;
                HugoMetrics.Count(HugoMetricCatalog.SecretRedactions, count, HugoMetrics.Tags("kind", "known"));
            }

            foreach (ScrubPattern pattern in _Patterns)
            {
                int count = 0;
                try
                {
                    result = pattern.Regex.Replace(result, match =>
                    {
                        count++;
                        string prefix = pattern.Keep == null ? String.Empty : match.Result(pattern.Keep);
                        return prefix + "[redacted:" + pattern.Kind + "]";
                    });
                }
                catch (RegexMatchTimeoutException)
                {
                    // A pathological input: fail closed by replacing the whole text.
                    redactions++;
                    HugoMetrics.Count(HugoMetricCatalog.SecretRedactions, 1, HugoMetrics.Tags("kind", "timeout"));
                    return "[redacted:timeout]";
                }
                if (count > 0)
                {
                    redactions += count;
                    HugoMetrics.Count(HugoMetricCatalog.SecretRedactions, count, HugoMetrics.Tags("kind", pattern.Kind));
                }
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private static int Count(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        #endregion
    }
}
