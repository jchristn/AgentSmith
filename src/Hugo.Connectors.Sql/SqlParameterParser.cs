namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Converts the free-form <c>parameters</c> tool argument (a JSON object of name to scalar) into bound parameter
    /// values. The map is caller-defined, so it is read with the JSON reader rather than a fixed typed class.
    /// Thread-safe.
    /// </summary>
    public static class SqlParameterParser
    {
        #region Private-Members

        private static readonly Regex _NamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a JSON object of parameters. Strings, numbers, booleans, and null are bound as typed values; arrays
        /// and objects are bound as their JSON text. A leading <c>@</c>, <c>:</c>, or <c>$</c> on a name is removed.
        /// </summary>
        /// <param name="json">JSON object text, or null for none.</param>
        /// <param name="maxParameters">Maximum number of parameters. Default 100.</param>
        /// <returns>Parameters; empty when none.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_argument</c> for malformed input.</exception>
        public static List<SqlParameterValue> Parse(string? json, int maxParameters = 100)
        {
            List<SqlParameterValue> values = new List<SqlParameterValue>();
            if (String.IsNullOrWhiteSpace(json)) return values;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                        throw new ConnectorException("invalid_argument", "Argument 'parameters' must be a JSON object of name to value.");
                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    {
                        string name = property.Name.TrimStart('@', ':', '$');
                        if (!_NamePattern.IsMatch(name))
                            throw new ConnectorException("invalid_argument", "Parameter name '" + property.Name + "' is invalid; use letters, digits, and underscores.");
                        if (values.Count >= maxParameters)
                            throw new ConnectorException("invalid_argument", "Too many parameters; the maximum is " + maxParameters + ".");
                        values.Add(new SqlParameterValue(name, Convert(property.Value)));
                    }
                }
            }
            catch (JsonException e)
            {
                throw new ConnectorException("invalid_argument", "Argument 'parameters' is not valid JSON: " + e.Message, e);
            }
            return values;
        }

        #endregion

        #region Private-Methods

        private static object? Convert(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out long l)) return l;
                    if (value.TryGetDecimal(out decimal d)) return d;
                    return value.GetDouble();
                default:
                    return value.GetRawText();
            }
        }

        #endregion
    }
}
