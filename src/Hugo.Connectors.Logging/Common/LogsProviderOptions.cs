namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed SolarWinds Observability filters for <c>logs_search</c> and <c>logs_tail</c>. Unknown
    /// options are rejected so the calling LLM learns which filters apply.
    /// </summary>
    public class LogsProviderOptions
    {
        #region Public-Members

        /// <summary>
        /// Argument name.
        /// </summary>
        public static readonly string ArgumentName = "providerOptions";

        /// <summary>
        /// SolarWinds Observability log group name.
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

        private static readonly string[] _Known = new string[] { "group", "entityId", "direction" };
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
