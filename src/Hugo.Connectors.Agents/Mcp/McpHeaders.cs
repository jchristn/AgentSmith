namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds the HTTP headers sent to an external MCP server from the resource's secret slots: the
    /// <c>authorizationHeader</c> token (sent as <c>Authorization: Bearer &lt;token&gt;</c> unless the value already
    /// starts with a scheme such as <c>Basic</c> or <c>Bearer</c>) and the fields of the <c>extraHeaders</c> secret
    /// (field name is the header name). Thread-safe.
    /// </summary>
    public static class McpHeaders
    {
        #region Public-Members

        /// <summary>
        /// Headers the transport owns; they cannot be set through <c>extraHeaders</c>.
        /// </summary>
        public static readonly IReadOnlyList<string> Reserved = new List<string>
        {
            "Host", "Content-Type", "Content-Length", "Accept", "Connection", "Transfer-Encoding", "Mcp-Session-Id",
            "MCP-Protocol-Version", "Last-Event-ID", "Cookie", "Expect", "Upgrade", "TE", "Keep-Alive", "Proxy-Authorization"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the headers for a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Header name and value pairs. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static List<KeyValuePair<string, string>> Build(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
            string? token = resource.Secrets.Get(McpSettingKeys.AuthorizationHeaderSlot, McpSettingKeys.TokenField);
            if (!String.IsNullOrWhiteSpace(token)) headers.Add(new KeyValuePair<string, string>("Authorization", AuthorizationValue(token)));

            foreach (KeyValuePair<string, string> extra in ExtraHeaders(resource))
            {
                if (!IsValidName(extra.Key) || IsReserved(extra.Key) || !IsValidValue(extra.Value)) continue;
                if (String.Equals(extra.Key, "Authorization", StringComparison.OrdinalIgnoreCase) && headers.Any(h => String.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase))) continue;
                headers.Add(extra);
            }
            return headers;
        }

        /// <summary>
        /// The Authorization header value for a token: unchanged when it already starts with a scheme followed by a
        /// space (for example <c>Basic dXNlcjpwYXNz</c>), otherwise <c>Bearer &lt;token&gt;</c>.
        /// </summary>
        /// <param name="token">Token. Null is treated as empty.</param>
        /// <returns>Header value.</returns>
        public static string AuthorizationValue(string? token)
        {
            string value = (token ?? String.Empty).Trim();
            int space = value.IndexOf(' ');
            if (space > 0 && IsValidName(value.Substring(0, space)) && value.Substring(space + 1).Trim().Length > 0) return value;
            return "Bearer " + value;
        }

        /// <summary>
        /// The <c>extraHeaders</c> secret's fields, read through the field names supplied by the host. Because
        /// <see cref="SecretAccessor"/> exposes values by known field names only, the secret must also carry a field
        /// <c>names</c> listing the header names (comma or newline separated); each listed name is read as a field.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Name and value pairs; empty when the slot is unbound.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static List<KeyValuePair<string, string>> ExtraHeaders(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            if (!resource.Secrets.HasSlot(McpSettingKeys.ExtraHeadersSlot)) return list;
            string? names = resource.Secrets.Get(McpSettingKeys.ExtraHeadersSlot, McpSettingKeys.ExtraHeaderNamesField);
            if (String.IsNullOrWhiteSpace(names)) return list;
            foreach (string name in names.Split(new char[] { ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string? value = resource.Secrets.Get(McpSettingKeys.ExtraHeadersSlot, name);
                if (value == null) continue;
                list.Add(new KeyValuePair<string, string>(name, value));
            }
            return list;
        }

        /// <summary>
        /// Whether a header name is an RFC 9110 token.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidName(string? name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            foreach (char c in name)
            {
                if (c >= 'a' && c <= 'z') continue;
                if (c >= 'A' && c <= 'Z') continue;
                if (c >= '0' && c <= '9') continue;
                if ("!#$%&'*+-.^_`|~".IndexOf(c) >= 0) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Whether a header value is free of line breaks and NUL characters.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidValue(string? value)
        {
            if (value == null) return false;
            return !value.Any(c => c == '\r' || c == '\n' || c == '\0');
        }

        /// <summary>
        /// Whether a header is owned by the transport.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True when reserved.</returns>
        public static bool IsReserved(string? name)
        {
            return !String.IsNullOrEmpty(name) && Reserved.Any(r => String.Equals(r, name, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
