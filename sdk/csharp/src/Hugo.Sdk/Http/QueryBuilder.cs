namespace Hugo.Sdk.Http
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Hugo.Sdk.Enumeration;

    /// <summary>
    /// Builds query parameter dictionaries. Null and empty values are skipped by the transport.
    /// </summary>
    public static class QueryBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Query parameters for an enumeration query.
        /// </summary>
        /// <param name="query">Query, or null for server defaults.</param>
        /// <returns>Parameters.</returns>
        public static Dictionary<string, string?> From(EnumerationQuery? query)
        {
            Dictionary<string, string?> result = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (query == null) return result;
            result["maxResults"] = query.MaxResults.ToString(CultureInfo.InvariantCulture);
            result["skip"] = query.Skip.ToString(CultureInfo.InvariantCulture);
            result["ordering"] = query.Ordering.ToString();
            result["search"] = query.Search;
            if (query.ActiveOnly) result["activeOnly"] = "true";
            if (!query.IncludeBuiltIn) result["includeBuiltIn"] = "false";
            return result;
        }

        /// <summary>
        /// Format a date for a query parameter (ISO 8601, UTC).
        /// </summary>
        /// <param name="value">Date, or null.</param>
        /// <returns>Formatted date, or null.</returns>
        public static string? Date(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// Format an integer for a query parameter.
        /// </summary>
        /// <param name="value">Value, or null.</param>
        /// <returns>Formatted value, or null.</returns>
        public static string? Int(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// Encode parameters as a query string (without the question mark). Null and empty values are skipped.
        /// </summary>
        /// <param name="query">Parameters, or null.</param>
        /// <returns>Query string, or empty.</returns>
        public static string Encode(IDictionary<string, string?>? query)
        {
            if (query == null) return String.Empty;
            return String.Join("&", query.Where(q => !String.IsNullOrEmpty(q.Value)).Select(q => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value!)));
        }

        #endregion
    }
}
