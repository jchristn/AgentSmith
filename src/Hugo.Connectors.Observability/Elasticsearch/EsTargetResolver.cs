namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Resolves an index expression that already passed <see cref="EsIndexGuard"/> to the concrete indexes, aliases,
    /// and data streams it reaches (<c>GET _resolve/index</c>), so that a search or count never touches an index
    /// outside the allow list through an alias or a wildcard that also matches an alias. When everything the
    /// expression reaches is allowed, the expression is sent unchanged. Otherwise the allowed concrete targets are sent
    /// instead, so totals and aggregations cover only them; when none is allowed, or the rewritten list would be too
    /// long for a request line, the call is refused with <c>permission_denied</c>. Thread-safe.
    /// </summary>
    public class EsTargetResolver
    {
        #region Public-Members

        /// <summary>
        /// Longest rewritten expression, in characters, sent in place of the caller's. Elasticsearch and OpenSearch
        /// limit the request line to 4 KiB by default. Default 3000, minimum 100, maximum 100000.
        /// </summary>
        public int MaxExpressionLength
        {
            get
            {
                return _MaxExpressionLength;
            }
            set
            {
                _MaxExpressionLength = Math.Clamp(value, 100, 100000);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxExpressionLength = 3000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve an expression against the allow list.
        /// </summary>
        /// <param name="transport">Transport of the resource.</param>
        /// <param name="expression">Expression that passed <see cref="EsIndexGuard.Require"/>.</param>
        /// <param name="allowed">Allowed patterns.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The expression to send and the targets left out.</returns>
        /// <exception cref="ArgumentNullException">Thrown when transport or expression is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>permission_denied</c> when the expression reaches only
        /// targets outside the allow list, or too many allowed ones to list.</exception>
        public async Task<EsResolvedTarget> ResolveAsync(ObservabilityTransport transport, string expression, IList<string>? allowed, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(transport);
            ArgumentNullException.ThrowIfNull(expression);
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>> { new KeyValuePair<string, string?>("expand_wildcards", "open") };
            string json = await transport.GetAsync("_resolve/index/" + expression, q, token).ConfigureAwait(false);
            EsResolveResponse response = ObservabilityJson.DeserializeUpstream<EsResolveResponse>(json, transport.Provider);
            return Decide(expression, response, allowed);
        }

        /// <summary>
        /// Decide what to send for a resolved expression. Pure; used by <see cref="ResolveAsync"/>.
        /// </summary>
        /// <param name="expression">Caller's expression.</param>
        /// <param name="response">Resolve response.</param>
        /// <param name="allowed">Allowed patterns.</param>
        /// <returns>The expression to send and the targets left out.</returns>
        /// <exception cref="ArgumentNullException">Thrown when expression or response is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>permission_denied</c> as described on <see cref="ResolveAsync"/>.</exception>
        public EsResolvedTarget Decide(string expression, EsResolveResponse response, IList<string>? allowed)
        {
            ArgumentNullException.ThrowIfNull(expression);
            ArgumentNullException.ThrowIfNull(response);

            SortedSet<string> keep = new SortedSet<string>(StringComparer.Ordinal);
            SortedSet<string> drop = new SortedSet<string>(StringComparer.Ordinal);
            List<string> viaAlias = new List<string>();
            foreach (EsResolvedIndex index in response.Indices ?? new List<EsResolvedIndex>())
                Sort(index.Name, allowed, keep, drop);
            foreach (EsResolvedAlias alias in response.Aliases ?? new List<EsResolvedAlias>())
            {
                foreach (string target in alias.Indices ?? new List<string>())
                {
                    if (!Sort(target, allowed, keep, drop) && !viaAlias.Contains(alias.Name)) viaAlias.Add(alias.Name);
                }
            }
            foreach (EsResolvedDataStream stream in response.DataStreams ?? new List<EsResolvedDataStream>())
                Sort(stream.Name, allowed, keep, drop);

            if (drop.Count == 0) return new EsResolvedTarget(expression, null);

            string through = viaAlias.Count == 0 ? String.Empty : " (through alias " + String.Join(", ", viaAlias) + ")";
            if (keep.Count == 0)
                throw new ConnectorException(ConnectorErrorCodes.PermissionDenied,
                    "Index expression '" + expression + "' reaches only indexes outside this resource's allowed patterns" + through + ". Use es_indices to see the indexes you may query.");

            string rewritten = String.Join(",", keep);
            if (rewritten.Length > _MaxExpressionLength)
                throw new ConnectorException(ConnectorErrorCodes.PermissionDenied,
                    "Index expression '" + expression + "' reaches " + drop.Count + " index(es) outside this resource's allowed patterns" + through
                    + ", and the " + keep.Count + " allowed ones are too many to list in one request. Use a narrower pattern that matches no alias pointing outside the allow list.");
            return new EsResolvedTarget(rewritten, drop.ToList());
        }

        #endregion

        #region Private-Methods

        private static bool Sort(string? name, IList<string>? allowed, SortedSet<string> keep, SortedSet<string> drop)
        {
            if (String.IsNullOrEmpty(name)) return true;
            if (EsIndexGuard.IsIndexAllowed(name, allowed))
            {
                keep.Add(name);
                return true;
            }
            drop.Add(name);
            return false;
        }

        #endregion
    }
}
