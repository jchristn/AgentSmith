namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// Argument helpers shared by the AWS tools: typed parsing of nested JSON arguments and identifier validation.
    /// Thread-safe.
    /// </summary>
    public static class AwsArguments
    {
        #region Private-Members

        private static readonly Regex _InstanceId = new Regex("^i-[0-9a-f]{8}([0-9a-f]{9})?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse the <c>filters</c> argument (an array of <c>{name, values}</c>).
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Argument name. Default <c>filters</c>.</param>
        /// <returns>Filters; empty when absent.</returns>
        /// <exception cref="ArgumentNullException">Thrown when arguments is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the value is malformed.</exception>
        public static List<AwsFilterArgument> Filters(ToolArguments arguments, string name = "filters")
        {
            ArgumentNullException.ThrowIfNull(arguments);
            List<AwsFilterArgument> filters = Typed<List<AwsFilterArgument>>(arguments, name) ?? new List<AwsFilterArgument>();
            foreach (AwsFilterArgument f in filters)
            {
                if (String.IsNullOrWhiteSpace(f.Name)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Every entry in " + name + " needs a name.");
                if (f.Values.Count == 0) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Filter '" + f.Name + "' needs at least one value.");
            }
            return filters;
        }

        /// <summary>
        /// Deserialize a nested argument into a typed value.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <returns>Value, or default when absent.</returns>
        /// <exception cref="ArgumentNullException">Thrown when arguments is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the value has the wrong shape.</exception>
        public static T? Typed<T>(ToolArguments arguments, string name)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? raw = arguments.GetRawJson(name);
            if (raw == null) return default;
            try
            {
                return AwsJson.Deserialize<T>(raw);
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + name + "' has the wrong shape: " + e.Message);
            }
        }

        /// <summary>
        /// Validate and normalize EC2 instance IDs.
        /// </summary>
        /// <param name="ids">IDs.</param>
        /// <returns>Distinct, trimmed IDs.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for malformed IDs.</exception>
        public static List<string> InstanceIds(IEnumerable<string>? ids)
        {
            List<string> list = (ids ?? Enumerable.Empty<string>()).Select(s => (s ?? String.Empty).Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            foreach (string id in list)
            {
                if (!IsInstanceId(id)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "'" + id + "' is not an EC2 instance ID (i- followed by 8 or 17 hex digits).");
            }
            return list;
        }

        /// <summary>
        /// Whether text is an EC2 instance ID.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>True when well-formed.</returns>
        public static bool IsInstanceId(string? value)
        {
            return !String.IsNullOrEmpty(value) && _InstanceId.IsMatch(value);
        }

        #endregion
    }
}
