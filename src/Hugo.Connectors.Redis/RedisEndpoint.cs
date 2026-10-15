namespace Hugo.Connectors.Redis
{
    using System;
    using System.Globalization;

    /// <summary>
    /// One <c>host:port</c> endpoint from the <c>endpoints</c> setting. Immutable and thread-safe.
    /// </summary>
    public class RedisEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Host name or IP address (IPv6 without brackets).
        /// </summary>
        public string Host { get; }

        /// <summary>
        /// TCP port, 1 to 65535.
        /// </summary>
        public int Port { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="port">Port.</param>
        /// <exception cref="ArgumentNullException">Thrown when host is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when port is outside 1 to 65535.</exception>
        public RedisEndpoint(string host, int port)
        {
            if (String.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Port must be from 1 to 65535.");
            Host = host;
            Port = port;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse <c>host</c>, <c>host:port</c>, or <c>[ipv6]:port</c>.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="defaultPort">Port used when the text has none.</param>
        /// <param name="endpoint">Parsed endpoint, or null.</param>
        /// <param name="error">Error message when parsing fails, else empty.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParse(string? text, int defaultPort, out RedisEndpoint? endpoint, out string error)
        {
            endpoint = null;
            error = String.Empty;
            string value = (text ?? String.Empty).Trim();
            if (value.Length == 0)
            {
                error = "An endpoint is empty.";
                return false;
            }

            if (value.Contains("://", StringComparison.Ordinal))
            {
                error = "Endpoint '" + value + "' must be host:port without a scheme. Use useSsl for TLS.";
                return false;
            }

            string host;
            string? portText = null;
            if (value.StartsWith('['))
            {
                int close = value.IndexOf(']');
                if (close < 0)
                {
                    error = "Endpoint '" + value + "' has an unclosed IPv6 bracket.";
                    return false;
                }
                host = value.Substring(1, close - 1);
                string rest = value.Substring(close + 1);
                if (rest.Length > 0)
                {
                    if (!rest.StartsWith(':'))
                    {
                        error = "Endpoint '" + value + "' must look like [ipv6]:port.";
                        return false;
                    }
                    portText = rest.Substring(1);
                }
            }
            else
            {
                int colon = value.LastIndexOf(':');
                if (colon >= 0 && value.IndexOf(':') != colon)
                {
                    error = "Endpoint '" + value + "' looks like an IPv6 address; write it as [address]:port.";
                    return false;
                }
                host = colon >= 0 ? value.Substring(0, colon) : value;
                if (colon >= 0) portText = value.Substring(colon + 1);
            }

            if (host.Length == 0 || host.IndexOfAny(new char[] { ' ', '\t', '/', '\\', ',', '\0' }) >= 0)
            {
                error = "Endpoint '" + value + "' has an invalid host.";
                return false;
            }

            int port = defaultPort;
            if (portText != null)
            {
                if (!Int32.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                {
                    error = "Endpoint '" + value + "' has an invalid port; use 1 to 65535.";
                    return false;
                }
            }

            endpoint = new RedisEndpoint(host, port);
            return true;
        }

        /// <summary>
        /// Text form, <c>host:port</c> (IPv6 hosts in brackets).
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            string host = Host.Contains(':', StringComparison.Ordinal) ? "[" + Host + "]" : Host;
            return host + ":" + Port.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
