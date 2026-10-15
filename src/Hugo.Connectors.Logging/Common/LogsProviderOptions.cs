namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed view of the <c>providerOptions</c> argument shared by <c>logs_search</c>, <c>logs_tail</c>, and
    /// <c>logs_volume</c>. Each connector rejects options it does not support, so the calling LLM learns which
    /// filters apply.
    /// </summary>
    public class LogsProviderOptions
    {
        #region Public-Members

        /// <summary>
        /// Argument name.
        /// </summary>
        public static readonly string ArgumentName = "providerOptions";

        /// <summary>
        /// Papertrail system ID or name.
        /// </summary>
        public string? System { get; set; } = null;

        /// <summary>
        /// Papertrail group ID or name, or SolarWinds Observability log group name.
        /// </summary>
        public string? Group { get; set; } = null;

        /// <summary>
        /// SolarWinds Observability entity ID.
        /// </summary>
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// SolarWinds Observability direction: <c>backward</c>, <c>forward</c>, or <c>tail</c>.
        /// </summary>
        public string? Direction { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly string[] _Known = new string[] { "system", "group", "entityId", "direction" };
        private static readonly string[] _Directions = new string[] { "backward", "forward", "tail" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the options from tool arguments.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Options. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when arguments is null.</exception>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_argument</c> for unknown keys or values.</exception>
        public static LogsProviderOptions FromArguments(ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            Dictionary<string, string> map = arguments.GetStringMap(ArgumentName);
            List<string> unknown = map.Keys.Where(k => !_Known.Contains(k, StringComparer.Ordinal)).ToList();
            if (unknown.Any())
                throw new ConnectorException("invalid_argument", "Unknown providerOptions key(s): " + String.Join(", ", unknown) + ". Known keys: " + String.Join(", ", _Known) + ".");

            LogsProviderOptions options = new LogsProviderOptions();
            options.System = Clean(map, "system");
            options.Group = Clean(map, "group");
            options.EntityId = Clean(map, "entityId");
            options.Direction = Clean(map, "direction");
            if (options.Direction != null)
            {
                string d = options.Direction.ToLowerInvariant();
                if (!_Directions.Contains(d, StringComparer.Ordinal))
                    throw new ConnectorException("invalid_argument", "providerOptions.direction must be one of backward, forward, or tail.");
                options.Direction = d;
            }
            return options;
        }

        /// <summary>
        /// Reject options a provider does not support.
        /// </summary>
        /// <param name="providerName">Provider display name.</param>
        /// <param name="system">Whether <c>system</c> is supported.</param>
        /// <param name="entityId">Whether <c>entityId</c> is supported.</param>
        /// <param name="direction">Whether <c>direction</c> is supported.</param>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_argument</c> when an unsupported option is set.</exception>
        public void Require(string providerName, bool system, bool entityId, bool direction)
        {
            List<string> unsupported = new List<string>();
            if (!system && System != null) unsupported.Add("system");
            if (!entityId && EntityId != null) unsupported.Add("entityId");
            if (!direction && Direction != null) unsupported.Add("direction");
            if (unsupported.Any())
                throw new ConnectorException("invalid_argument", providerName + " does not support providerOptions." + String.Join(", providerOptions.", unsupported) + ".");
        }

        #endregion

        #region Private-Methods

        private static string? Clean(Dictionary<string, string> map, string key)
        {
            if (!map.TryGetValue(key, out string? value)) return null;
            if (String.IsNullOrWhiteSpace(value) || value == "null") return null;
            return value.Trim();
        }

        #endregion
    }
}
