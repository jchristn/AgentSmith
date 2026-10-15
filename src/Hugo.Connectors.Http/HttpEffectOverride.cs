namespace Hugo.Connectors.Http
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One <c>methodEffectOverrides</c> entry: <c>METHOD /prefix=Effect</c>, for example <c>POST /search=Read</c>. The
    /// method may be <c>*</c> for every method. The prefix is relative to the base URL path and matches on a segment
    /// boundary. Immutable and thread-safe.
    /// </summary>
    public class HttpEffectOverride
    {
        #region Public-Members

        /// <summary>
        /// Upper-case method, or <c>*</c>.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Normalized prefix starting with <c>/</c> and without a trailing slash (except the root <c>/</c>).
        /// </summary>
        public string Prefix { get; }

        /// <summary>
        /// Effect applied when the override matches.
        /// </summary>
        public EffectEnum Effect { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="method">Method or <c>*</c>.</param>
        /// <param name="prefix">Prefix starting with <c>/</c>.</param>
        /// <param name="effect">Effect.</param>
        /// <exception cref="ArgumentNullException">Thrown when method or prefix is null or empty.</exception>
        public HttpEffectOverride(string method, string prefix, EffectEnum effect)
        {
            if (String.IsNullOrWhiteSpace(method)) throw new ArgumentNullException(nameof(method));
            if (String.IsNullOrWhiteSpace(prefix)) throw new ArgumentNullException(nameof(prefix));
            Method = method.Trim().ToUpperInvariant();
            Prefix = HttpPathGuard.NormalizePrefix(prefix);
            Effect = effect;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse one entry.
        /// </summary>
        /// <param name="line">Entry text, for example <c>POST /search=Read</c>.</param>
        /// <param name="value">Parsed override, or null on failure.</param>
        /// <param name="error">Reason on failure; empty on success.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParse(string? line, out HttpEffectOverride? value, out string error)
        {
            value = null;
            error = String.Empty;
            string text = (line ?? String.Empty).Trim();
            int eq = text.LastIndexOf('=');
            if (eq <= 0 || eq == text.Length - 1)
            {
                error = "Expected METHOD /prefix=Effect, for example POST /search=Read.";
                return false;
            }

            string left = text.Substring(0, eq).Trim();
            string effectText = text.Substring(eq + 1).Trim();
            string[] parts = left.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                error = "Expected a method and a path prefix separated by a space before '=', for example POST /search=Read.";
                return false;
            }

            string method = parts[0];
            if (method != "*" && !HttpRequestClassifier.IsMethodToken(method))
            {
                error = "'" + method + "' is not an HTTP method or '*'.";
                return false;
            }

            if (!HttpPathGuard.IsValidPrefix(parts[1], out string prefixError))
            {
                error = prefixError;
                return false;
            }

            if (effectText.Length == 0 || !Enum.TryParse<EffectEnum>(effectText, true, out EffectEnum effect) || !Enum.IsDefined(typeof(EffectEnum), effect) || Char.IsDigit(effectText[0]))
            {
                error = "Effect must be Read, Write, or Destructive.";
                return false;
            }

            value = new HttpEffectOverride(method, parts[1], effect);
            return true;
        }

        /// <summary>
        /// Text form.
        /// </summary>
        /// <returns><c>METHOD /prefix=Effect</c>.</returns>
        public override string ToString()
        {
            return Method + " " + Prefix + "=" + Effect;
        }

        #endregion
    }
}
