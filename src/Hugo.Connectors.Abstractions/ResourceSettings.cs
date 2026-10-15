namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>
    /// Typed, read-only accessor over a resource's settings. Values are stored as strings and converted on read; a missing
    /// value falls back to the descriptor default supplied by the caller. Thread-safe for reads.
    /// </summary>
    public class ResourceSettings
    {
        #region Private-Members

        private readonly Dictionary<string, string> _Values;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate from raw string values keyed by setting key.
        /// </summary>
        /// <param name="values">Values. Null is treated as empty.</param>
        public ResourceSettings(IDictionary<string, string>? values)
        {
            _Values = values == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(values, StringComparer.Ordinal);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Keys that have a value.
        /// </summary>
        /// <returns>Keys.</returns>
        public IReadOnlyCollection<string> Keys()
        {
            return _Values.Keys;
        }

        /// <summary>
        /// Whether a non-empty value exists for a key.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <returns>True when present.</returns>
        public bool Has(string key)
        {
            return !String.IsNullOrEmpty(key) && _Values.TryGetValue(key, out string? value) && !String.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Read a string value.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="fallback">Value returned when missing or empty.</param>
        /// <returns>Value or fallback.</returns>
        public string? GetString(string key, string? fallback = null)
        {
            if (String.IsNullOrEmpty(key)) return fallback;
            if (_Values.TryGetValue(key, out string? value) && !String.IsNullOrEmpty(value)) return value;
            return fallback;
        }

        /// <summary>
        /// Read a required string value.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <returns>Value.</returns>
        /// <exception cref="ConnectorException">Thrown when the value is missing.</exception>
        public string GetRequiredString(string key)
        {
            string? value = GetString(key);
            if (String.IsNullOrEmpty(value)) throw new ConnectorException("setting_missing", "Required setting '" + key + "' is not set.");
            return value;
        }

        /// <summary>
        /// Read an integer value.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="fallback">Value returned when missing or unparseable.</param>
        /// <returns>Value or fallback.</returns>
        public long GetLong(string key, long fallback)
        {
            string? value = GetString(key);
            if (value != null && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) return parsed;
            return fallback;
        }

        /// <summary>
        /// Read an integer value clamped to a range.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="fallback">Value returned when missing or unparseable.</param>
        /// <param name="minimum">Minimum.</param>
        /// <param name="maximum">Maximum.</param>
        /// <returns>Clamped value.</returns>
        public int GetInt(string key, int fallback, int minimum = Int32.MinValue, int maximum = Int32.MaxValue)
        {
            long value = GetLong(key, fallback);
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return (int)value;
        }

        /// <summary>
        /// Read a boolean value. Accepts <c>true</c>, <c>false</c>, <c>1</c>, <c>0</c>, <c>yes</c>, <c>no</c>.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="fallback">Value returned when missing or unparseable.</param>
        /// <returns>Value or fallback.</returns>
        public bool GetBool(string key, bool fallback)
        {
            string? value = GetString(key);
            if (value == null) return fallback;
            string v = value.Trim().ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes") return true;
            if (v == "false" || v == "0" || v == "no") return false;
            return fallback;
        }

        /// <summary>
        /// Read a string list (newline separated). Blank lines are removed and entries trimmed.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <returns>Values; empty when missing.</returns>
        public List<string> GetStringList(string key)
        {
            string? value = GetString(key);
            if (value == null) return new List<string>();
            return value.Split('\n')
                .Select(s => s.Trim().TrimEnd('\r'))
                .Where(s => s.Length > 0)
                .ToList();
        }

        #endregion
    }
}
