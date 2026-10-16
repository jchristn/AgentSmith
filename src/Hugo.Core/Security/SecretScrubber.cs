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
    /// tool output (P2.9, plan section 12.5). Two layers: pattern redaction with protected structural keys and markers, then exact plaintext values
    /// registered on this instance (including base64 and URL-encoded forms). Patterns for private key blocks, AWS access key identifiers and secret keys,
    /// bearer tokens, URI and connection string passwords, password pairs, and generic token assignments. Every
    /// redaction is replaced with [redacted:kind] and counted in hugo_secret_redactions_total. Thread-safe.
    /// </summary>
    public class SecretScrubber
    {
        #region Public-Members

        /// <summary>
        /// Shared bounded known-value and pattern scrubber for logs, request history, and audit descriptions.
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
        /// Maximum registered encoded variants. Default 100000, minimum 100, maximum 1000000.
        /// At capacity, all subsequent nonempty output is redacted until ClearKnownValues, preventing eviction leaks.
        /// </summary>
        public int MaximumKnownValues
        {
            get { lock (_Lock) { return _MaximumKnownValues; } }
            set { lock (_Lock) { _MaximumKnownValues = Math.Clamp(value, 100, 1000000); if (_Known.Count > _MaximumKnownValues) { _Known.Clear(); _KnownCharacters = 0; _SortedKnown = null; _Saturated = true; } } }
        }

        /// <summary>
        /// Maximum retained characters across known variants. Default 4194304 (about 8 MiB UTF-16), minimum 1024,
        /// maximum 33554432. Overflow redacts all subsequent shared output until ClearKnownValues.
        /// </summary>
        public int MaximumKnownCharacters
        {
            get { lock (_Lock) { return _MaximumKnownCharacters; } }
            set { lock (_Lock) { _MaximumKnownCharacters = Math.Clamp(value, 1024, 33554432); if (_KnownCharacters > _MaximumKnownCharacters) { _Known.Clear(); _KnownCharacters = 0; _SortedKnown = null; _Saturated = true; } } }
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
            new ScrubPattern("quoted_assignment", new Regex(@"(?i)\b(password|passwd|pwd|passphrase|secret|client[_\-]?secret|api[_\-]?key|access[_\-]?token|refresh[_\-]?token|auth[_\-]?token|x-secret-key|x-api-key)(\s*[""']?\s*[:=]\s*)([""'])(?:\\.|(?!\3)[\s\S])*(?=\3)", RegexOptions.Compiled, _Timeout), "$1$2$3"),
            new ScrubPattern("private_key", new Regex(@"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----[\s\S]*?(?:-----END (?:[A-Z0-9]+ )*PRIVATE KEY-----|$)", RegexOptions.Compiled, _Timeout), null),
            new ScrubPattern("aws_access_key", new Regex(@"\b(?:AKIA|ASIA|AGPA|AIDA|AROA|ANPA|ANVA|AIPA)[0-9A-Z]{16}\b", RegexOptions.Compiled, _Timeout), null),
            new ScrubPattern("aws_secret_key", new Regex(@"(?i)(aws_secret_access_key|secretAccessKey|aws_session_token|sessionToken)(\s*[""']?\s*[:=]\s*[""']?)[A-Za-z0-9/+=]{16,}", RegexOptions.Compiled, _Timeout), "$1$2"),
            new ScrubPattern("bearer", new Regex(@"(?i)\b(bearer\s+)[A-Za-z0-9\-._~+/]{8,}=*", RegexOptions.Compiled, _Timeout), "$1"),
            new ScrubPattern("connection_string", new Regex(@"(?i)\b([a-z][a-z0-9+.\-]*://[^:/\s@]+:)[^@\s/]+(@)", RegexOptions.Compiled, _Timeout), "$1$2"),
            new ScrubPattern("password_pair", new Regex(@"(?i)\b(password|passwd|pwd|passphrase)(\s*[""']?\s*[:=]\s*[""']?)[^;\r\n,&""'}]+", RegexOptions.Compiled, _Timeout), "$1$2"),
            new ScrubPattern("generic", new Regex(@"(?i)\b(secret|client[_\-]?secret|api[_\-]?key|access[_\-]?token|refresh[_\-]?token|auth[_\-]?token|x-secret-key|x-api-key)(\s*[""']?\s*[:=]\s*[""']?)[^;\r\n,&""'}]+", RegexOptions.Compiled, _Timeout), "$1$2")
        };

        private readonly object _Lock = new object();
        private readonly HashSet<string> _Known = new HashSet<string>(StringComparer.Ordinal);
        private int _MinimumKnownValueLength = 4;
        private int _MaximumKnownValues = 100000;
        private bool _Saturated;
        private int _MaximumKnownCharacters = 4194304;
        private int _KnownCharacters;
        private string[]? _SortedKnown;

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
                if (_Saturated) return;
                if (value.Length > _MaximumKnownCharacters) { _Saturated = true; return; }
                List<string> variants = new List<string> { value, Convert.ToBase64String(Encoding.UTF8.GetBytes(value)), Uri.EscapeDataString(value) };
                string trimmed = value.Trim();
                if (trimmed.Length >= _MinimumKnownValueLength) variants.Add(trimmed);
                foreach (string variant in variants.Distinct(StringComparer.Ordinal))
                {
                    if (_Known.Contains(variant)) continue;
                    if (_Known.Count >= _MaximumKnownValues || (long)_KnownCharacters + variant.Length > _MaximumKnownCharacters) { _Saturated = true; return; }
                    _Known.Add(variant);
                    _KnownCharacters += variant.Length;
                    _SortedKnown = null;
                }
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
                _Saturated = false;
                _KnownCharacters = 0;
                _SortedKnown = null;
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

            string[] known;
            lock (_Lock)
            {
                if (_Saturated)
                {
                    redactions = 1;
                    return "[redacted:capacity]";
                }
                known = _SortedKnown ??= _Known.OrderByDescending(k => k.Length).ToArray();
            }
            List<ScrubSegment> segments = new List<ScrubSegment> { new ScrubSegment(text, false) };
            foreach (ScrubPattern pattern in _Patterns)
            {
                Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
                List<ScrubSegment> next = new List<ScrubSegment>();
                // Later outer assignments must still span an earlier inner redaction. Protection is for exact-known
                // substitution, not a barrier to subsequent pattern recognition.
                string combined = String.Concat(segments.Select(segment => segment.Text));
                try
                {
                    int offset = 0;
                    int segmentIndex = 0;
                    int segmentOffset = 0;
                    foreach (Match match in pattern.Regex.Matches(combined))
                    {
                        Transfer(segments, next, ref segmentIndex, ref segmentOffset, match.Index - offset, true);
                        Transfer(segments, next, ref segmentIndex, ref segmentOffset, match.Length, false);
                        string kind = pattern.Kind == "quoted_assignment" ? AssignmentKind(match.Groups[1].Value) : pattern.Kind;
                        string prefix = pattern.Keep == null ? String.Empty : match.Result(pattern.Keep);
                        // Recognized key names are structural, even if a password literally equals "password".
                        // URI prefixes can contain caller material, so retain exact-match scrubbing there.
                        if (kind == "connection_string")
                        {
                            prefix = ScrubKnown(prefix, known, out int prefixCount);
                            redactions += prefixCount;
                            if (prefixCount > 0) HugoMetrics.Count(HugoMetricCatalog.SecretRedactions, prefixCount, HugoMetrics.Tags("kind", "known"));
                        }
                        next.Add(new ScrubSegment(prefix + "[redacted:" + kind + "]", true));
                        offset = match.Index + match.Length;
                        counts.TryGetValue(kind, out int count);
                        counts[kind] = count + 1;
                    }
                    Transfer(segments, next, ref segmentIndex, ref segmentOffset, combined.Length - offset, true);
                }
                catch (RegexMatchTimeoutException)
                {
                    redactions++;
                    HugoMetrics.Count(HugoMetricCatalog.SecretRedactions, 1, HugoMetrics.Tags("kind", "timeout"));
                    return "[redacted:timeout]";
                }
                segments = next;
                foreach (KeyValuePair<string, int> count in counts)
                {
                    redactions += count.Value;
                    HugoMetrics.Count(HugoMetricCatalog.SecretRedactions, count.Value, HugoMetrics.Tags("kind", count.Key));
                }
            }
            StringBuilder result = new StringBuilder();
            foreach (ScrubSegment segment in segments)
            {
                if (segment.ProtectedText) result.Append(segment.Text);
                else
                {
                    result.Append(ScrubKnown(segment.Text, known, out int count));
                    redactions += count;
                    if (count > 0) HugoMetrics.Count(HugoMetricCatalog.SecretRedactions, count, HugoMetrics.Tags("kind", "known"));
                }
            }
            return result.ToString();
        }

        #endregion

        #region Private-Methods

        private static string AssignmentKind(string key)
        {
            return key.Equals("password", StringComparison.OrdinalIgnoreCase) || key.Equals("passwd", StringComparison.OrdinalIgnoreCase) || key.Equals("pwd", StringComparison.OrdinalIgnoreCase) || key.Equals("passphrase", StringComparison.OrdinalIgnoreCase) ? "password_pair" : "generic";
        }

        private static void Transfer(List<ScrubSegment> source, List<ScrubSegment> target, ref int index, ref int offset, int length, bool keep)
        {
            while (length > 0)
            {
                ScrubSegment segment = source[index];
                int take = Math.Min(length, segment.Text.Length - offset);
                if (keep) target.Add(new ScrubSegment(segment.Text.Substring(offset, take), segment.ProtectedText));
                length -= take;
                offset += take;
                if (offset == segment.Text.Length) { index++; offset = 0; }
            }
        }

        private static string ScrubKnown(string text, string[] known, out int count)
        {
            count = 0;
            if (known.Length == 0 || text.Length == 0) return text;
            StringBuilder result = new StringBuilder();
            int offset = 0;
            while (offset < text.Length)
            {
                int first = -1;
                string? selected = null;
                // The snapshot is longest-first, so equal-position overlaps redact the whole longest value.
                foreach (string value in known)
                {
                    int index = text.IndexOf(value, offset, StringComparison.Ordinal);
                    if (index < 0 || (first >= 0 && index >= first)) continue;
                    first = index;
                    selected = value;
                }
                if (first < 0 || selected == null) { result.Append(text, offset, text.Length - offset); break; }
                result.Append(text, offset, first - offset);
                result.Append("[redacted:known]");
                offset = first + selected.Length;
                count++;
            }
            return result.ToString();
        }

        #endregion
    }
}
