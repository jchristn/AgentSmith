namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The settings of one <c>mcp</c> resource, parsed with defaults and clamped to their ranges. Parsing never throws;
    /// problems are reported by <see cref="McpConnector.ValidateAsync"/> and, at call time, by <see cref="RequireUrl"/>.
    /// Not thread-safe for mutation; treat as immutable after <see cref="FromSettings"/>.
    /// </summary>
    public class McpResourceOptions
    {
        #region Public-Members

        /// <summary>
        /// Transport. Default StreamableHttp. An unknown value parses as StreamableHttp and is reported by validation.
        /// </summary>
        public McpTransportEnum Transport { get; set; } = McpTransportEnum.StreamableHttp;

        /// <summary>
        /// Endpoint URL, or null when missing or not an absolute http or https URL without credentials.
        /// </summary>
        public Uri? Url { get; set; } = null;

        /// <summary>
        /// Stdio executable, or empty.
        /// </summary>
        public string Command { get; set; } = String.Empty;

        /// <summary>
        /// Stdio arguments. Never null.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Resource-level Stdio opt-in. Default false.
        /// </summary>
        public bool AllowStdio { get; set; } = false;

        /// <summary>
        /// Include and exclude filters. Never null.
        /// </summary>
        public McpToolFilter Filter { get; set; } = new McpToolFilter(null, null);

        /// <summary>
        /// How long a discovered tool list is reused. Minimum 1 minute, maximum 1440 minutes, default 15 minutes.
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Longest time one remote call may take. Minimum 1 second, maximum 600 seconds, default 60 seconds.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static McpResourceOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            McpResourceOptions o = new McpResourceOptions();
            TryParseTransport(settings.GetString(McpSettingKeys.Transport), out McpTransportEnum transport);
            o.Transport = transport;
            if (TryParseUrl(settings.GetString(McpSettingKeys.Url), out Uri? url, out string _)) o.Url = url;
            o.Command = (settings.GetString(McpSettingKeys.Command) ?? String.Empty).Trim();
            o.Arguments = settings.GetStringList(McpSettingKeys.Arguments);
            o.AllowStdio = settings.GetBool(McpSettingKeys.AllowStdio, false);
            o.Filter = new McpToolFilter(settings.GetStringList(McpSettingKeys.IncludeTools), settings.GetStringList(McpSettingKeys.ExcludeTools));
            o.RefreshInterval = TimeSpan.FromMinutes(settings.GetInt(McpSettingKeys.RefreshIntervalMinutes, 15, 1, 1440));
            o.Timeout = TimeSpan.FromSeconds(settings.GetInt(McpSettingKeys.TimeoutSeconds, 60, 1, 600));
            return o;
        }

        /// <summary>
        /// Parse a transport name (case-insensitive).
        /// </summary>
        /// <param name="value">Value. Null or empty means StreamableHttp.</param>
        /// <param name="transport">Parsed transport, StreamableHttp when unknown.</param>
        /// <returns>True when the value is empty or a known transport name.</returns>
        public static bool TryParseTransport(string? value, out McpTransportEnum transport)
        {
            transport = McpTransportEnum.StreamableHttp;
            if (String.IsNullOrWhiteSpace(value)) return true;
            string v = value.Trim();
            if (String.Equals(v, "StreamableHttp", StringComparison.OrdinalIgnoreCase) || String.Equals(v, "http", StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(v, "Stdio", StringComparison.OrdinalIgnoreCase))
            {
                transport = McpTransportEnum.Stdio;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Parse an endpoint URL: absolute http or https, no user info or fragment.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="url">Parsed URL.</param>
        /// <param name="error">Reason on failure.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParseUrl(string? value, out Uri? url, out string error)
        {
            url = null;
            error = String.Empty;
            if (String.IsNullOrWhiteSpace(value))
            {
                error = "url is required for the StreamableHttp transport, for example https://mcp.example.com/mcp.";
                return false;
            }
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                error = "url must be an absolute http or https URL including the MCP path, for example https://mcp.example.com/mcp.";
                return false;
            }
            if (!String.IsNullOrEmpty(parsed.UserInfo))
            {
                error = "url must not contain credentials; bind a secret to the authorizationHeader slot instead.";
                return false;
            }
            if (!String.IsNullOrEmpty(parsed.Fragment))
            {
                error = "url must not contain a fragment.";
                return false;
            }
            url = parsed;
            return true;
        }

        /// <summary>
        /// The endpoint URL, or an error when it is missing or invalid.
        /// </summary>
        /// <returns>URL.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when the URL is unusable.</exception>
        public Uri RequireUrl()
        {
            if (Url == null) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The resource's url is missing or is not an absolute http or https URL without credentials.");
            return Url;
        }

        /// <summary>
        /// Name of the remote endpoint for messages: the URL without its query, or the Stdio command.
        /// </summary>
        /// <returns>Endpoint description.</returns>
        public string EndpointName()
        {
            if (Transport == McpTransportEnum.Stdio) return "stdio:" + Command;
            if (Url == null) return "(no url)";
            return Url.GetLeftPart(UriPartial.Path);
        }

        #endregion
    }
}
