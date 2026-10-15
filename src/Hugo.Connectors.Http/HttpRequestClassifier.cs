namespace Hugo.Connectors.Http
{
    using System;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classifies one <c>http_request</c> call. Pure and thread-safe.
    /// <list type="number">
    /// <item>A method that is not an HTTP token, or not in the resource's <c>allowedMethods</c>, is rejected.</item>
    /// <item>A path that fails <see cref="HttpPathGuard"/> (malformed, escaping, or outside the allowed prefixes) is rejected.</item>
    /// <item>A matching <c>methodEffectOverrides</c> entry decides the effect. The longest matching prefix wins, and a
    /// specific method beats <c>*</c> on the same prefix.</item>
    /// <item>Otherwise GET, HEAD, and OPTIONS are Read; POST, PUT, and PATCH are Write; DELETE and every other method
    /// (TRACE, CONNECT, WebDAV, custom) are Destructive, failing closed.</item>
    /// </list>
    /// </summary>
    public static class HttpRequestClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify a call.
        /// </summary>
        /// <param name="method">HTTP method argument. Null means GET.</param>
        /// <param name="path">Path argument relative to the base URL. Null means <c>/</c>.</param>
        /// <param name="options">Resource options.</param>
        /// <param name="target">Binding alias or resource name for the approval summary.</param>
        /// <returns>Classification.</returns>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public static EffectClassification Classify(string? method, string? path, HttpResourceOptions options, string? target)
        {
            ArgumentNullException.ThrowIfNull(options);
            string m = String.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();
            if (!IsMethodToken(m)) return EffectClassification.Reject("'" + Shorten(method) + "' is not a valid HTTP method");
            if (!options.AllowedMethods.Contains(m)) return EffectClassification.Reject("method " + m + " is not in this resource's allowedMethods (" + String.Join(", ", options.AllowedMethods.OrderBy(x => x, StringComparer.Ordinal)) + ")");

            HttpPathCheck check = HttpPathGuard.Check(path, options.AllowedPathPrefixes);
            if (!check.Allowed) return EffectClassification.Reject(check.Reason);

            string summary = m + " " + check.Path + (String.IsNullOrEmpty(target) ? String.Empty : " on " + target);
            HttpEffectOverride? match = options.Overrides
                .Where(o => (o.Method == m || o.Method == "*") && HttpPathGuard.MatchesPrefix(check.Path, o.Prefix))
                .OrderByDescending(o => o.Prefix == "/" ? 0 : o.Prefix.Length)
                .ThenBy(o => o.Method == "*" ? 1 : 0)
                .FirstOrDefault();
            if (match != null) return new EffectClassification(match.Effect, "override " + match + " matches " + m + " " + check.Path, summary);

            EffectEnum effect = BaseEffect(m);
            string reason = effect == EffectEnum.Read || m == "POST" || m == "PUT" || m == "PATCH" || m == "DELETE"
                ? "method " + m + " is " + effect
                : "method " + m + " is not a standard read or write method, so it is treated as Destructive";
            return new EffectClassification(effect, reason, summary);
        }

        /// <summary>
        /// Effect of a method without overrides.
        /// </summary>
        /// <param name="method">Upper-case method.</param>
        /// <returns>Effect.</returns>
        public static EffectEnum BaseEffect(string method)
        {
            switch ((method ?? String.Empty).ToUpperInvariant())
            {
                case "GET":
                case "HEAD":
                case "OPTIONS":
                    return EffectEnum.Read;
                case "POST":
                case "PUT":
                case "PATCH":
                    return EffectEnum.Write;
                default:
                    return EffectEnum.Destructive;
            }
        }

        /// <summary>
        /// Whether a value is an RFC 9110 token (valid as a method or header name).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsToken(string? value)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0;
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Whether a value is a valid HTTP method token.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsMethodToken(string? value)
        {
            return IsToken(value);
        }

        #endregion

        #region Private-Methods

        private static string Shorten(string? value)
        {
            string v = (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ');
            return v.Length > 40 ? v.Substring(0, 37) + "..." : v;
        }

        #endregion
    }
}
