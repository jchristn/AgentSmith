namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Refuses durable context that carries secrets, credentials, personal data, or raw result rows. The server's
    /// scrubber is the backstop; this guard rejects the content before it is stored so the caller can fix it.
    /// Thread-safe for concurrent <see cref="Check"/> calls once configured.
    /// </summary>
    public class KnowledgeContentGuard
    {
        #region Public-Members

        /// <summary>
        /// Maximum characters of one stored entry. Minimum 200, maximum 200000, default 16000.
        /// </summary>
        public int MaxEntryChars
        {
            get
            {
                return _MaxEntryChars;
            }
            set
            {
                _MaxEntryChars = Math.Clamp(value, 200, 200000);
            }
        }

        /// <summary>
        /// Maximum table-like data lines (Markdown table rows or tab-separated lines) allowed, so a short list of
        /// enumeration values passes but pasted result sets do not. Minimum 0, maximum 1000, default 12.
        /// </summary>
        public int MaxTableLines
        {
            get
            {
                return _MaxTableLines;
            }
            set
            {
                _MaxTableLines = Math.Clamp(value, 0, 1000);
            }
        }

        /// <summary>
        /// Maximum distinct email addresses allowed (personal data). Minimum 0, maximum 1000, default 2.
        /// </summary>
        public int MaxEmailAddresses
        {
            get
            {
                return _MaxEmailAddresses;
            }
            set
            {
                _MaxEmailAddresses = Math.Clamp(value, 0, 1000);
            }
        }

        /// <summary>
        /// Minimum length of a bound secret value that is searched for in the content. Shorter values cause false
        /// positives. Minimum 1, maximum 1000, default 6.
        /// </summary>
        public int MinSecretLength
        {
            get
            {
                return _MinSecretLength;
            }
            set
            {
                _MinSecretLength = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _PrivateKey = new Regex("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant);
        private static readonly Regex _Assignment = new Regex(
            "\\b(password|passwd|pwd|secret|client[_-]?secret|api[_-]?key|access[_-]?key|secret[_-]?key|auth[_-]?token|access[_-]?token|refresh[_-]?token|private[_-]?key)\\b[\"'`]?\\s*[:=]\\s*[\"'`]?[^\\s\"'`;,]{4,}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex _AwsKey = new Regex("\\b(AKIA|ASIA)[0-9A-Z]{16}\\b", RegexOptions.CultureInvariant);
        private static readonly Regex _Bearer = new Regex("\\bbearer\\s+[A-Za-z0-9._~+/=-]{20,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex _Jwt = new Regex("\\beyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant);
        private static readonly Regex _ProviderToken = new Regex("\\b(ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|xox[abprs]-[A-Za-z0-9-]{10,}|sk-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{30,})", RegexOptions.CultureInvariant);
        private static readonly Regex _ConnectionString = new Regex("(user id|uid|username|user)\\s*=\\s*[^;]+;[^\\n]*(password|pwd)\\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex _UrlCredential = new Regex("[a-z][a-z0-9+.-]*://[^/\\s:@]+:[^/\\s@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex _Email = new Regex("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", RegexOptions.CultureInvariant);
        private int _MaxEntryChars = 16000;
        private int _MaxTableLines = 12;
        private int _MaxEmailAddresses = 2;
        private int _MinSecretLength = 6;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public KnowledgeContentGuard()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check content before it is stored.
        /// </summary>
        /// <param name="content">Content.</param>
        /// <param name="secretValues">Secret values bound to the resource (never echoed), or null.</param>
        /// <returns>Null when acceptable, otherwise the reason it was refused (never containing the offending value).</returns>
        public string? Check(string? content, IEnumerable<string>? secretValues)
        {
            if (String.IsNullOrWhiteSpace(content)) return "Context is empty.";
            if (content.Length > _MaxEntryChars) return "Context is " + content.Length + " characters; the limit per entry is " + _MaxEntryChars + ". Write a shorter, curated version.";

            if (secretValues != null && secretValues.Any(s => !String.IsNullOrEmpty(s) && s.Length >= _MinSecretLength && content.Contains(s, StringComparison.Ordinal)))
                return "Context contains a credential value bound to this resource. Remove it; context must never hold secrets.";
            if (_PrivateKey.IsMatch(content)) return "Context contains a private key. Remove it; context must never hold secrets.";
            if (_Assignment.IsMatch(content)) return "Context appears to assign a password, key, or token value. Remove the value; describe where credentials live instead.";
            if (_AwsKey.IsMatch(content) || _Bearer.IsMatch(content) || _Jwt.IsMatch(content) || _ProviderToken.IsMatch(content))
                return "Context contains what looks like an access key or token. Remove it; context must never hold secrets.";
            if (_ConnectionString.IsMatch(content) || _UrlCredential.IsMatch(content)) return "Context contains a connection string or URL with credentials. Remove it.";

            int emails = _Email.Matches(content).Select(m => m.Value.ToLowerInvariant()).Distinct().Count();
            if (emails > _MaxEmailAddresses) return "Context contains " + emails + " email addresses, which looks like personal data or raw rows. Describe the column instead of copying values.";

            int tableLines = content.Split('\n').Count(IsDataLine);
            if (tableLines > _MaxTableLines) return "Context contains " + tableLines + " table-like data lines, which looks like raw query results. Summarize what the data means instead of copying rows.";
            return null;
        }

        #endregion

        #region Private-Methods

        private static bool IsDataLine(string line)
        {
            string t = line.Trim();
            if (t.Length == 0) return false;
            if (t.StartsWith("|", StringComparison.Ordinal) && t.Count(c => c == '|') >= 3)
                return !t.Replace("|", String.Empty).Replace("-", String.Empty).Replace(":", String.Empty).Trim().All(Char.IsWhiteSpace);
            return t.Count(c => c == '\t') >= 2;
        }

        #endregion
    }
}
